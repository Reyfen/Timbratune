using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Reyfen.Timbratune.Audio.MediaFoundation;

// The few Media Foundation calls the Source Reader needs (mfapi.h, mfidl.h, mfreadwrite.h,
// mfobjects.h). COM interfaces are source-generated; slots that are never called are declared
// only to keep the vtable order and take no arguments.

internal static partial class Mf
{
    public const uint Version = 0x00020070;           // MF_VERSION (MF_SDK_VERSION 2, MF_API_VERSION 0x70)
    public const uint StartupLite = 1;                 // MFSTARTUP_LITE: no network sources
    public const uint AllStreams = 0xFFFFFFFE;         // MF_SOURCE_READER_ALL_STREAMS
    public const uint FirstAudioStream = 0xFFFFFFFD;   // MF_SOURCE_READER_FIRST_AUDIO_STREAM
    public const uint MediaSource = 0xFFFFFFFF;        // MF_SOURCE_READER_MEDIASOURCE
    public const uint EndOfStream = 0x2;               // MF_SOURCE_READERF_ENDOFSTREAM
    public const uint CurrentMediaTypeChanged = 0x20;  // MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED
    public const ushort VtUi8 = 21;                    // VT_UI8

    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");          // MF_MT_MAJOR_TYPE
    public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");            // MF_MT_SUBTYPE
    public static readonly Guid Audio = new("73647561-0000-0010-8000-00aa00389b71");              // MFMediaType_Audio
    public static readonly Guid Float = new("00000003-0000-0010-8000-00aa00389b71");              // MFAudioFormat_Float
    public static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");           // MF_MT_AUDIO_NUM_CHANNELS
    public static readonly Guid SamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");   // MF_MT_AUDIO_SAMPLES_PER_SECOND
    public static readonly Guid Duration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");           // MF_PD_DURATION

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(out IMFMediaType mediaType);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>Releases a COM object now instead of when the GC finds it (samples hold decoder buffers).</summary>
    public static void Release(object? o)
    {
        if (o is ComObject com) com.FinalRelease();
    }
}

/// <summary>PROPVARIANT, enough for the VT_UI8 duration.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1, Reserved2, Reserved3;
    public ulong Value;
    public nint Extra;
}

[GeneratedComInterface]
[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
internal partial interface IMFAttributes
{
    void GetItem();
    void GetItemType();
    void CompareItem();
    void Compare();
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    void GetUINT64();
    void GetDouble();
    void GetGUID();
    void GetStringLength();
    void GetString();
    void GetAllocatedString();
    void GetBlobSize();
    void GetBlob();
    void GetAllocatedBlob();
    void GetUnknown();
    void SetItem();
    void DeleteItem();
    void DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    void SetUINT64();
    void SetDouble();
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    void SetString();
    void SetBlob();
    void SetUnknown();
    void LockStore();
    void UnlockStore();
    void GetCount();
    void GetItemByIndex();
    void CopyAllItems();
}

[GeneratedComInterface]
[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
internal partial interface IMFMediaType : IMFAttributes
{
    void GetMajorType();
    void IsCompressedFormat();
    void IsEqual();
    void GetRepresentation();
    void FreeRepresentation();
}

[GeneratedComInterface]
[Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
internal unsafe partial interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out byte* buffer, out uint maxLength, out uint currentLength);
    [PreserveSig] int Unlock();
    void GetCurrentLength();
    void SetCurrentLength();
    void GetMaxLength();
}

[GeneratedComInterface]
[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
internal partial interface IMFSample : IMFAttributes
{
    void GetSampleFlags();
    void SetSampleFlags();
    void GetSampleTime();
    void SetSampleTime();
    void GetSampleDuration();
    void SetSampleDuration();
    void GetBufferCount();
    void GetBufferByIndex();
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    void AddBuffer();
    void RemoveBufferByIndex();
    void RemoveAllBuffers();
    void GetTotalLength();
    void CopyToBuffer();
}

[GeneratedComInterface]
[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
internal unsafe partial interface IMFSourceReader
{
    void GetStreamSelection();
    [PreserveSig] int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
    void GetNativeMediaType();
    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType mediaType);
    [PreserveSig] int SetCurrentMediaType(uint streamIndex, nint reserved, IMFMediaType mediaType);
    void SetCurrentPosition();
    [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags,
        out long timestamp, out IMFSample? sample);
    void Flush();
    void GetServiceForStream();
    [PreserveSig] int GetPresentationAttribute(uint streamIndex, in Guid attribute, PropVariant* value);
}
