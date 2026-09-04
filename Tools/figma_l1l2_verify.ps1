# Figma L1/L2 验证引导脚本
# 用法：
#   1) 设置 FIGMA_TOKEN（或已有环境变量）
#   2) pwsh -File figma_l1l2_verify.ps1 -FileKey bl985lo1stxpBBV94SQlY6 -NodeId 16-73
#
# 步骤：
#   A. --discover 列出 Figma 节点树（找到组件 frame 的 node-id）
#   B. 拉节点 JSON → figma_to_uss.py 生成 USS
#   C. 把 USS 里的关键值（主色/字号/间距/圆角）打出来
#   D. 提示用户在 Unity 里对照
param(
    [string]$FileKey = "bl985lo1stxpBBV94SQlY6",
    [string]$NodeId = "",
    [string]$Token = ""
)

if (-not $Token) { $Token = $env:FIGMA_TOKEN }
if (-not $Token) {
    $patFile = "D:\AIWorkSpace\a2ui-unity-toolkit\Temp\figma_pat.local"
    if (Test-Path $patFile) { $Token = (Get-Content $patFile -Raw).Trim() }
}
if (-not $Token) {
    Write-Host "需要 Figma PAT：设环境变量 FIGMA_TOKEN 或写 Temp\figma_pat.local"
    Write-Host "可先不带 -NodeId 只跑 --discover"
    exit 1
}

$py = "python"
$tools = "D:\AIWorkSpace\a2ui-unity-toolkit\Tools"

if (-not $NodeId) {
    Write-Host "=== Step A: discover 节点树 ==="
    & $py "$tools\figma_api_export.py" --token $Token --file-key $FileKey --discover --depth 4
    Write-Host "`n请找到包含组件（Button/Card/Text）的 Frame，记下 node-id，重跑本脚本加 -NodeId <id>"
    exit 0
}

Write-Host "=== Step B: 拉节点 JSON + 转 USS ==="
& $py "$tools\figma_api_export.py" --token $Token --file-key $FileKey --node-id $NodeId --convert

Write-Host "`n=== Step C: FigmaExport USS 关键值 ==="
$tokens = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Styles\FigmaExport\FigmaTokens.uss"
$comps = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Styles\FigmaExport\FigmaComponents.uss"

Write-Host "--- FigmaTokens.uss（语义色/字号/间距/圆角）---"
Get-Content $tokens | Select-String -Pattern "--a2ui-(color-primary|type-h|type-body|space-[12]|radius-)" | ForEach-Object { $_.Line.Trim() }

Write-Host "`n--- FigmaComponents.uss（组件尺寸）---"
Get-Content $comps | Select-String -Pattern "(btn--primary|padding|border-radius|font-size)" | Select-Object -First 15 | ForEach-Object { $_.Line.Trim() }

Write-Host @"

=== Step D: Unity 侧对照 ===
1. 打开编辑器 → Play A2UITestBed → 推 demos/figma_button_demo
2. 主题切到 FigmaExport
3. 用菜单 A2UI Scheme A → 捕获当前卡片截图
4. 并排对比 Figma 设计稿：
   - L1 色彩：主色 #FF5C00 是否一致？
   - L2 字号：body=16px 是否一致？按钮高度 48px 是否一致？
   - L3 约束：圆角 10px / 间距 10px 是否一致？
"@