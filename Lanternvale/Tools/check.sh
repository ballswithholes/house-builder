#!/usr/bin/env bash
# Lanternvale verification without Unity.
#
#   Tools/check.sh data  [args]   validate JSON data only (compiles Core/Json+Data+Util; robust while Rules is being edited)
#   Tools/check.sh core  [args]   compile the whole pure-C# core, validate data, run [Test]s (add --sim for [Sim]s)
#   Tools/check.sh unity          compile-check every Unity script against Unity reference assemblies (editor + player)
#                                 and every shader pass (HLSL via glslangValidator, see Tools/shadercheck)
#   Tools/check.sh shaders        shaders only
#   Tools/check.sh all   [args]   core + unity
#
# Harness args: --grep <text> (only print data problems containing text), --filter <test name>, --sim,
#               --no-tests, --allow-problems, --quiet
# Set LV_BUILD_DIR to a private directory when several agents build concurrently.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CACHE="$ROOT/Tools/.cache"
BUILD="${LV_BUILD_DIR:-$CACHE/build}"
MODE="${1:-all}"; shift || true
SCRIPTS="$ROOT/Assets/Lanternvale/Scripts"
HARNESS="$ROOT/Tools/harness/CoreTests"

gen_core_proj() { # $1 = name, $2 = include rules+tests (true/false)
  local dir="$BUILD/$1"; mkdir -p "$dir"
  local includes
  if [ "$2" = "true" ]; then
    includes="<Compile Include=\"$SCRIPTS/Core/**/*.cs\" /><Compile Include=\"$HARNESS/*.cs\" />"
  else
    includes="<Compile Include=\"$SCRIPTS/Core/Json/**/*.cs;$SCRIPTS/Core/Data/**/*.cs;$SCRIPTS/Core/Util/**/*.cs\" /><Compile Include=\"$HARNESS/Program.cs\" />"
  fi
  cat > "$dir/$1.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS0649;CS0414;CS0169;CS0162</NoWarn><AssemblyName>CoreTests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>$includes</ItemGroup>
</Project>
PROJ
  echo "$dir/$1.csproj"
}

gen_unity_proj() { # $1 = name, $2 = player (true/false)
  local dir="$BUILD/$1"; mkdir -p "$dir"
  local defines="UNITY_2021_3_OR_NEWER;UNITY_STANDALONE;ENABLE_LEGACY_INPUT_MANAGER"
  local editorRef="<Reference Include=\"UnityEditor\"><HintPath>$CACHE/unityrefs/UnityEditor.dll</HintPath><Private>false</Private></Reference>"
  local remove=""
  # player config also drops the legacy input define so the IMGUI-input code path gets compiled too
  if [ "$2" = "true" ]; then remove="<Compile Remove=\"$SCRIPTS/Editor/**/*.cs\" />"; editorRef=""; defines="UNITY_2021_3_OR_NEWER;UNITY_STANDALONE;ENABLE_INPUT_SYSTEM"; else defines="UNITY_EDITOR;$defines"; fi
  cat > "$dir/$1.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework><LangVersion>9.0</LangVersion><Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS0649;CS0414;CS0169;CS0618;CS0162</NoWarn><DefineConstants>$defines</DefineConstants>
  </PropertyGroup>
  <ItemGroup><Compile Include="$SCRIPTS/**/*.cs" />$remove</ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine"><HintPath>$CACHE/unityrefs/UnityEngine.dll</HintPath><Private>false</Private></Reference>
    $editorRef
  </ItemGroup>
  <ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="all" /></ItemGroup>
</Project>
PROJ
  echo "$dir/$1.csproj"
}

fetch_unity_refs() {
  if [ -f "$CACHE/unityrefs/UnityEngine.dll" ]; then return; fi
  mkdir -p "$CACHE/unityrefs"
  echo "Fetching Unity reference assemblies (NuGet Unity3D.SDK 2021.1.14.1)..."
  curl -sSL -o "$CACHE/unity3d.sdk.nupkg" https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg
  (cd "$CACHE" && unzip -o -q unity3d.sdk.nupkg 'lib/*.dll' -d sdk && cp sdk/lib/*.dll unityrefs/)
}

run_core() { # $1 = name, $2 = full; remaining = harness args
  local name="$1" full="$2"; shift 2
  local proj; proj="$(gen_core_proj "$name" "$full")"
  if ! dotnet build "$proj" -nologo -v q -clp:ErrorsOnly -o "$BUILD/$name/out" > "$BUILD/$name/build.log" 2>&1; then
    grep -E "error" "$BUILD/$name/build.log" | sed "s|$ROOT/||" | sort -u | head -60; echo "BUILD FAILED ($name)"; return 1
  fi
  local extra=()
  if [ "$full" != "true" ]; then extra+=(--no-tests); fi
  # ${a[@]+"${a[@]}"}: bash < 4.4 (macOS /bin/bash 3.2) treats an empty array / no arguments as unbound under set -u
  dotnet "$BUILD/$name/out/CoreTests.dll" --data "$ROOT/Assets/Lanternvale/Resources/Data" ${extra[@]+"${extra[@]}"} ${1+"$@"}
}

run_unity() {
  fetch_unity_refs
  for cfg in editor player; do
    echo "== Unity scripts ($cfg configuration)"
    local p="false"; [ "$cfg" = "player" ] && p="true"
    local proj; proj="$(gen_unity_proj "UnityCheck_$cfg" "$p")"
    if dotnet build "$proj" -nologo -v q -clp:ErrorsOnly -o "$BUILD/UnityCheck_$cfg/out" > "$BUILD/UnityCheck_$cfg/build.log" 2>&1; then
      echo "compile OK"
    else
      grep -E "error" "$BUILD/UnityCheck_$cfg/build.log" | sed "s|$ROOT/||" | sort -u | head -80; echo "UNITY COMPILE FAILED ($cfg)"; return 1
    fi
  done
}

case "$MODE" in
  data)  echo "== Data validation"; run_core DataCheck false ${1+"$@"} ;;
  core)  echo "== Core (rules engine, data validation, tests)"; run_core CoreTests true ${1+"$@"} ;;
  unity) status=0; run_unity || status=1; echo "== Shaders"; python3 "$ROOT/Tools/shadercheck/check.py" || status=1; exit $status ;;
  shaders) echo "== Shaders"; python3 "$ROOT/Tools/shadercheck/check.py" ;;
  all)   status=0; echo "== Core"; run_core CoreTests true ${1+"$@"} || status=1; run_unity || status=1
         echo "== Shaders"; python3 "$ROOT/Tools/shadercheck/check.py" || status=1; exit $status ;;
  *)     echo "usage: $0 data|core|unity|shaders|all [args]"; exit 2 ;;
esac
