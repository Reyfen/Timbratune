using System.Reflection;
using System.Reflection.Emit;

namespace Reyfen.Timbratune.Architecture.Tests;

/// <summary>
/// The types a type uses: its signatures (base type, interfaces, fields, properties, events,
/// methods) and every type, method and field token in its method bodies' IL. Lambdas, async
/// state machines and local functions compile to nested types, which count as their own
/// types here but carry the namespace of the type that declares them.
/// Reflection only: no package beyond the BCL.
/// </summary>
internal static class TypeReferences
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>Every type defined in <paramref name="assembly"/>, nested ones included.</summary>
    public static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    /// <summary>The full names of the types <paramref name="type"/> uses (generic arguments and element types unwrapped).</summary>
    public static IReadOnlySet<string> Of(Type type)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Add(Type? t)
        {
            if (t is null || t.IsGenericParameter) return;
            if (t.HasElementType)
            {
                Add(t.GetElementType());
                return;
            }
            if (t.IsGenericType)
            {
                foreach (var a in t.GetGenericArguments()) Add(a);
                if (!t.IsGenericTypeDefinition) t = t.GetGenericTypeDefinition();
            }
            if (t.FullName is { } name && found.Add(name) && t.DeclaringType is { } outer) Add(outer);
        }

        Safe(() => Add(type.BaseType));
        Safe(() => { foreach (var i in type.GetInterfaces()) Add(i); });
        Safe(() => { foreach (var a in type.GetCustomAttributesData()) Add(a.AttributeType); });
        foreach (var f in Safe(() => type.GetFields(All)) ?? []) Safe(() => Add(f.FieldType));
        foreach (var p in Safe(() => type.GetProperties(All)) ?? []) Safe(() => Add(p.PropertyType));
        foreach (var e in Safe(() => type.GetEvents(All)) ?? []) Safe(() => Add(e.EventHandlerType));

        var methods = (Safe(() => type.GetMethods(All)) ?? []).Cast<MethodBase>()
            .Concat(Safe(() => type.GetConstructors(All)) ?? []);
        foreach (var m in methods)
        {
            if (m is MethodInfo mi) Safe(() => Add(mi.ReturnType));
            Safe(() => { foreach (var p in m.GetParameters()) Add(p.ParameterType); });
            Safe(() => { foreach (var l in m.GetMethodBody()?.LocalVariables ?? []) Add(l.LocalType); });
            var il = Safe(() => m.GetMethodBody()?.GetILAsByteArray());
            if (il is null) continue;
            foreach (var token in Tokens(il))
            {
                var member = Safe(() => m.Module.ResolveMember(token,
                    type.IsGenericType ? type.GetGenericArguments() : null,
                    m.IsGenericMethod ? m.GetGenericArguments() : null));
                switch (member)
                {
                    case Type t:
                        Add(t);
                        break;
                    case FieldInfo f:
                        Add(f.DeclaringType);
                        Safe(() => Add(f.FieldType));
                        break;
                    case MethodBase called:
                        Add(called.DeclaringType);
                        if (called is MethodInfo cm) Safe(() => Add(cm.ReturnType));
                        if (called.IsGenericMethod) Safe(() => { foreach (var a in called.GetGenericArguments()) Add(a); });
                        break;
                }
            }
        }
        return found;
    }

    /// <summary>The metadata tokens (types, methods, fields) in a method body.</summary>
    private static IEnumerable<int> Tokens(byte[] il)
    {
        for (var i = 0; i < il.Length;)
        {
            short value = il[i++];
            if (value == 0xFE) value = unchecked((short)(0xFE00 | il[i++]));
            if (!OpCodesByValue.TryGetValue(value, out var op)) yield break; // not IL we understand: stop
            switch (op.OperandType)
            {
                case OperandType.InlineType:
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineTok:
                    yield return BitConverter.ToInt32(il, i);
                    i += 4;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + 4 * BitConverter.ToInt32(il, i);
                    break;
                default: // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR
                    i += 4;
                    break;
            }
        }
    }

    private static T? Safe<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or ArgumentException or BadImageFormatException
                                      or NotSupportedException or InvalidOperationException or MissingMemberException)
        {
            return default;
        }
    }

    private static void Safe(Action f) => Safe(() =>
    {
        f();
        return 0;
    });
}
