#!/bin/sh
export LD_LIBRARY_PATH=/work/unity/libs/usr/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH
export HOME=${HOME:-/work/unity/home}
# gVisor: make realtime thread-priority requests no-ops so FMOD (Unity audio, FSBTool) can init. See AGENTS.md §9.
export LD_PRELOAD=/work/unity/shim/libschedfix.so${LD_PRELOAD:+:$LD_PRELOAD}
exec /work/unity/editor/Editor/Unity "$@"
