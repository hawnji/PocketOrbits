#!/usr/bin/env bash
# Downloads Unity reference assemblies from nuget.org into DotNet/.unityrefs (not committed).
# Used only to compile-check the Unity-facing code where no Unity editor is installed.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .unityrefs/tmp
fetch() { # id version
  local f=".unityrefs/tmp/$1.$2.nupkg"
  [ -f "$f" ] || curl -sSfL -o "$f" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg"
  unzip -o -q "$f" -d ".unityrefs/tmp/$1"
}
fetch unityengine.modules 2021.3.33
fetch unity3d.sdk 2021.1.14.1
cp .unityrefs/tmp/unityengine.modules/lib/netstandard2.0/*.dll .unityrefs/
cp .unityrefs/tmp/unity3d.sdk/lib/UnityEditor.dll .unityrefs/
echo "Unity reference assemblies in $(pwd)/.unityrefs"
