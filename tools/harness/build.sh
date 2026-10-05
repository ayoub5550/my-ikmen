#!/bin/bash
# Headless engine harness: compiles unity/Assets/IK/Scripts/Core with Roslyn (Unity's Mono) and
# runs a CPU-vs-CPU match without Unity. Usage:
#   UNITY_EDITOR_DIR=/path/to/Editor tools/harness/build.sh
#   $UNITY_EDITOR_DIR/Data/MonoBleedingEdge/bin/mono tools/harness/h.exe kfm_zss kfm [printEvery] [maxTicks]
set -e
E=${UNITY_EDITOR_DIR:-/work/unity/editor/Editor}
M=$E/Data/MonoBleedingEdge; U=$E/Data/Managed/UnityEngine
H=$(cd "$(dirname "$0")" && pwd); C=$H/../../unity/Assets/IK/Scripts/Core
$M/bin/mono $M/lib/mono/4.5/csc.exe -nologo -nowarn:414,169,649,219,162,0618 -langversion:9 -out:$H/h.exe \
  -r:$M/lib/mono/4.5/Facades/netstandard.dll -r:$U/UnityEngine.CoreModule.dll \
  -r:$U/UnityEngine.AudioModule.dll -r:$U/UnityEngine.ImageConversionModule.dll $C/*.cs $H/*.cs
