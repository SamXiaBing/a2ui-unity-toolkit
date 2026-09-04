# 清理坏 meta 文件名，让 Unity 重新生成
$compDir = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Samples\components"
Get-ChildItem $compDir -Filter "*.meta" | ForEach-Object {
    # 正确的 meta 名应该是 <file>.meta（双扩展名是对的）
    # 但脚本生成了错误的名字，直接全删让 Unity 重生成
    Remove-Item $_.FullName -Force
}
Write-Host "meta files removed - Unity will regenerate"
Get-ChildItem $compDir -Filter "*.jsonl" | ForEach-Object { $_.Name }
