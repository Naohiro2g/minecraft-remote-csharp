#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! -x "$1" ]]; then
  echo '使い方: bash prepare-project.sh /Unity/Editor/Unity' >&2
  exit 2
fi

unity_editor="$1"
demo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$demo_dir/.local/UnityProject"
mkdir -p "$demo_dir/.local"

run_unity() {
  local log_path="$1"
  shift
  if ! "$unity_editor" "$@" -logFile "$log_path"; then
    tail -n 50 "$log_path" >&2
    return 1
  fi
}

if [[ ! -f "$project_dir/ProjectSettings/ProjectVersion.txt" ]]; then
  if [[ -d "$project_dir" ]] && [[ -n "$(ls -A "$project_dir")" ]]; then
    echo "未完成のプロジェクトがあります。中身を確認してください: $project_dir" >&2
    exit 1
  fi
  run_unity "$demo_dir/.local/create.log" -batchmode -nographics -quit -createProject "$project_dir"
fi

# 編集元はこのリポのAssets。生成先で同じファイルを編集した場合は上書きする。
cp -R "$demo_dir/Assets/LocalSandboxDemo" "$project_dir/Assets/"
cp "$demo_dir/Assets/LocalSandboxDemo.meta" "$project_dir/Assets/LocalSandboxDemo.meta"
run_unity "$demo_dir/.local/scene.log" -batchmode -nographics -quit -projectPath "$project_dir" \
  -executeMethod Code2Create.UnitySandbox.LocalDemo.DemoEditor.CreateScene

echo "Unity Hubで次のプロジェクトを追加して開いてください:"
echo "$project_dir"
echo "作成されたLocalSandboxDemoシーンを開き、Playで開始します。"
