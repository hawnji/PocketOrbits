#!/usr/bin/env bash
# Everything that can be verified without a Unity editor:
#   1. EditMode tests (pure C# core) under dotnet + NUnit
#   2. Level validation + stored-solution replay (LevelLab check)
#   3. Compile check of runtime, editor and PlayMode test code against Unity reference assemblies
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root/DotNet"
echo "== EditMode tests (dotnet)"
dotnet test PocketOrbits.Core.Tests --nologo -v q
echo "== Level validation"
dotnet run --project LevelLab -v q -- check "$root/Assets/Game/Data/Resources/Levels"
echo "== Unity compile check"
[ -d .unityrefs ] || ./fetch-unity-refs.sh
dotnet build UnityCompileCheck -p:UnityTarget=Player --nologo -v q
dotnet build UnityCompileCheck -p:UnityTarget=Editor --nologo -v q
echo "All offline checks passed."
