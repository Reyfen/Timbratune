#!/usr/bin/env bash
# Builds the signed App Store .ipa on a Mac (Xcode, .NET 10 SDK and `dotnet workload install ios`),
# and with --upload sends it to TestFlight. .github/workflows/ios.yml runs this; it works on any
# Mac whose keychain holds the distribution certificate and that has the profile installed.
#
#   scripts/build-ios.sh [--build-number N] [--upload]
#
# Environment:
#   IOS_CODESIGN_KEY      signing identity (default: the first "Apple Distribution" one found)
#   IOS_PROVISION         provisioning profile name (default: "Timbratune AppStore")
#   ASC_KEY_ID, ASC_ISSUER_ID   App Store Connect API key, for --upload; the .p8 file must be at
#                         ~/.appstoreconnect/private_keys/AuthKey_<ASC_KEY_ID>.p8
# Output: publish/Timbratune-ios/Timbratune-v<Version>-ios.ipa
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
build_number=1
upload=false
while [ $# -gt 0 ]; do
  case "$1" in
    --build-number) build_number="$2"; shift 2 ;;
    --upload) upload=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)"
provision="${IOS_PROVISION:-Timbratune AppStore}"
key="${IOS_CODESIGN_KEY:-}"
if [ -z "$key" ]; then
  key="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Distribution[^"]*\)".*/\1/p' | head -1)"
fi
if [ -z "$key" ]; then
  echo "No \"Apple Distribution\" signing identity in the keychain (security find-identity -v -p codesigning)." >&2
  exit 1
fi
echo "Timbratune $version ($build_number), signed by \"$key\" with \"$provision\""

out="$root/publish/Timbratune-ios"
rm -rf "$out"
dotnet publish "$root/src/Reyfen.Timbratune.iOS/Reyfen.Timbratune.iOS.csproj" -c Release \
  -p:ApplicationVersion="$build_number" \
  -p:CodesignKey="$key" \
  -p:CodesignProvision="$provision" \
  -o "$out"

ipa="$(find "$out" -maxdepth 1 -name '*.ipa' | head -1)"
if [ -z "$ipa" ]; then echo "No .ipa in $out" >&2; exit 1; fi
final="$out/Timbratune-v$version-ios.ipa"
mv "$ipa" "$final"
find "$out" -mindepth 1 -not -name "$(basename "$final")" -exec rm -rf {} + 2>/dev/null || true
echo "Built $final"

if [ "$upload" = true ]; then
  : "${ASC_KEY_ID:?ASC_KEY_ID is needed for --upload}" "${ASC_ISSUER_ID:?ASC_ISSUER_ID is needed for --upload}"
  xcrun altool --upload-app --type ios --file "$final" --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"
  echo "Uploaded to App Store Connect; it shows in TestFlight once Apple has processed it."
fi
