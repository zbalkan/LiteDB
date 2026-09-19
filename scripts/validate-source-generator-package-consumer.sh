#!/usr/bin/env bash
set -euo pipefail

ROOT_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT_DIRECTORY

# shellcheck source=lib/platform.sh
. "$ROOT_DIRECTORY/scripts/lib/platform.sh"

# Run from the repository root and keep every path relative to it. Git Bash on Windows hands the
# .NET CLI a POSIX absolute path it cannot resolve, so relative paths keep one script portable.
cd "$ROOT_DIRECTORY"

RUNTIME_IDENTIFIER_VALUE="$(litedb_runtime_identifier)"
readonly RUNTIME_IDENTIFIER_VALUE
EXECUTABLE_SUFFIX="$(litedb_executable_suffix "$RUNTIME_IDENTIFIER_VALUE")"
readonly EXECUTABLE_SUFFIX

readonly PACKAGE_VERSION="0.0.0-sourcegenerator-p13.1"
readonly FEED_DIRECTORY="artifacts/source-generator-package-feed"
readonly PACKAGE_CACHE_DIRECTORY="artifacts/source-generator-package-cache"
readonly CONSUMER_PROJECT="LiteDB.SourceGenerator.PackageConsumer/LiteDB.SourceGenerator.PackageConsumer.csproj"
readonly CONSUMER_DIRECTORY="LiteDB.SourceGenerator.PackageConsumer"
readonly GENERATED_DIRECTORY="$CONSUMER_DIRECTORY/obj/generated"
readonly PUBLISH_DIRECTORY="artifacts/source-generator-package-consumer-native-aot"
readonly TRIMMED_DIRECTORY="artifacts/source-generator-package-consumer-trimmed"

printf '[PACKAGE-CONSUMER] Validating runtime identifier %s.\n' "$RUNTIME_IDENTIFIER_VALUE"

printf '%s\n' '[PACKAGE-CONSUMER] Preparing a clean local package feed and NuGet cache.'
rm -rf "$FEED_DIRECTORY" "$PACKAGE_CACHE_DIRECTORY" "$GENERATED_DIRECTORY" "$PUBLISH_DIRECTORY" "$TRIMMED_DIRECTORY" "$CONSUMER_DIRECTORY/bin" "$CONSUMER_DIRECTORY/obj"
mkdir -p "$FEED_DIRECTORY" "$PACKAGE_CACHE_DIRECTORY"

printf '%s\n' '[PACKAGE-CONSUMER] Packing the matching LiteDB runtime and source-generator package pair.'
dotnet pack "LiteDB/LiteDB.csproj" \
  --configuration Release \
  --nologo \
  -p:GitVersionEnabled=false \
  -p:PackageVersion="$PACKAGE_VERSION" \
  --output "$FEED_DIRECTORY"
dotnet pack "LiteDB.SourceGenerator/LiteDB.SourceGenerator.csproj" \
  --configuration Release \
  --nologo \
  -p:GitVersionEnabled=false \
  -p:PackageVersion="$PACKAGE_VERSION" \
  --output "$FEED_DIRECTORY"

test -f "$FEED_DIRECTORY/LiteDB.$PACKAGE_VERSION.nupkg"
test -f "$FEED_DIRECTORY/LiteDB.SourceGenerator.$PACKAGE_VERSION.nupkg"

printf '%s\n' '[PACKAGE-CONSUMER] Restoring the external consumer with the local package feed and required Native AOT toolchain source.'
NUGET_PACKAGES="$(litedb_native_path "$ROOT_DIRECTORY/$PACKAGE_CACHE_DIRECTORY")"
export NUGET_PACKAGES
dotnet restore "$CONSUMER_PROJECT" \
  --configfile "$CONSUMER_DIRECTORY/NuGet.config" \
  --no-cache \
  --force-evaluate \
  --runtime "$RUNTIME_IDENTIFIER_VALUE" \
  --nologo \
  -p:GitVersionEnabled=false

grep -q "\"LiteDB/$PACKAGE_VERSION\"" "$CONSUMER_DIRECTORY/obj/project.assets.json"
grep -q "\"LiteDB.SourceGenerator/$PACKAGE_VERSION\"" "$CONSUMER_DIRECTORY/obj/project.assets.json"
grep -q 'analyzers/dotnet/cs/LiteDB.SourceGenerator.dll' "$CONSUMER_DIRECTORY/obj/project.assets.json"

printf '%s\n' '[PACKAGE-CONSUMER] Building the restored consumer and verifying generated mapper output.'
dotnet build "$CONSUMER_PROJECT" \
  --configuration Release \
  --no-restore \
  --nologo \
  -p:GitVersionEnabled=false
GENERATED_MAPPING_FILES="$(find "$GENERATED_DIRECTORY" -name 'LiteDbGeneratedMappings*.g.cs' -print)"
GENERATED_MAPPING_FILE="${GENERATED_MAPPING_FILES%%$'\n'*}"
test -n "$GENERATED_MAPPING_FILE"
grep -q 'PackagedGeneratedRecord' "$GENERATED_MAPPING_FILE"
grep -q 'SerializeDynamicDictionary' "$GENERATED_MAPPING_FILE"
grep -q 'RegisterGeneratedExecutionMap' "$GENERATED_MAPPING_FILE"

printf '%s\n' '[PACKAGE-CONSUMER] Publishing and running the restored consumer as a trimmed, non-AOT executable.'
dotnet publish "$CONSUMER_PROJECT" \
  --configuration Release \
  --runtime "$RUNTIME_IDENTIFIER_VALUE" \
  --self-contained true \
  --no-restore \
  --nologo \
  -p:GitVersionEnabled=false \
  -p:PublishAot=false \
  -p:PublishTrimmed=true \
  --output "$TRIMMED_DIRECTORY"
"$TRIMMED_DIRECTORY/LiteDB.SourceGenerator.PackageConsumer$EXECUTABLE_SUFFIX"

printf '%s\n' '[PACKAGE-CONSUMER] Publishing and running the restored consumer as self-contained Native AOT.'
dotnet publish "$CONSUMER_PROJECT" \
  --configuration Release \
  --runtime "$RUNTIME_IDENTIFIER_VALUE" \
  --self-contained true \
  --no-restore \
  --nologo \
  -p:GitVersionEnabled=false \
  /p:IlcParallelism=1 \
  --output "$PUBLISH_DIRECTORY"
"$PUBLISH_DIRECTORY/LiteDB.SourceGenerator.PackageConsumer$EXECUTABLE_SUFFIX"

printf '[PACKAGE-CONSUMER] Passed: local-feed package restore, analyzer discovery, generated mapping, and Native AOT execution on %s.\n' "$RUNTIME_IDENTIFIER_VALUE"
