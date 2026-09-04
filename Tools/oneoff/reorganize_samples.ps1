# 样本重命名脚本：统一命名规范 <序号>_<类别>_<名称>.v0.8.jsonl
# 保留 categories: components/（单元测）、scenarios/（场景）、bench/（时间轴）
$ErrorActionPreference = "Stop"
$base = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Samples"

# ---- 1. 重组：把散落在根目录的样本归类 ----
$renames = @{
    # scenarios/ — Agent 交互场景（保留全部，统一前缀）
    "agent_charge_pick.v0.8.jsonl"        = "scenarios\agent_01_charge_pick.v0.8.jsonl"
    "agent_confirm_camera.v0.8.jsonl"     = "scenarios\agent_02_confirm_camera.v0.8.jsonl"
    "agent_decide_route.v0.8.jsonl"       = "scenarios\agent_03_decide_route.v0.8.jsonl"
    "agent_my_pet_board.v0.8.jsonl"       = "scenarios\agent_04_pet_board.v0.8.jsonl"
    "agent_pet_preference_grow.v0.8.jsonl"= "scenarios\agent_05_pet_preference.v0.8.jsonl"
    "agent_trip_conflict.v0.8.jsonl"      = "scenarios\agent_06_trip_conflict.v0.8.jsonl"
    "agent_wash_exception.v0.8.jsonl"     = "scenarios\agent_07_wash_exception.v0.8.jsonl"
    "prompt_confirm_hpa.v0.8.jsonl"       = "scenarios\agent_08_confirm_hpa.v0.8.jsonl"
    "02_login_screen_carstore.v0.8.jsonl" = "scenarios\app_01_login_screen.v0.8.jsonl"
    "dashboard_home.v0.8.jsonl"           = "scenarios\app_02_dashboard_home.v0.8.jsonl"
    "poi_complex.v0.8.jsonl"              = "scenarios\app_03_poi_nearby.v0.8.jsonl"

    # features/ — 功能演示（合并同质项：prompt_media≈media_card≈cabin_media 保留 1+1）
    "prompt_media.v0.8.jsonl"             = "features\media_player.v0.8.jsonl"
    "media_card.v0.8.jsonl"               = $null  # 与 media_player 高度重复，删除
    "cabin_media.v0.8.jsonl"              = "features\media_minibar.v0.8.jsonl"
    "prompt_climate.v0.8.jsonl"           = "features\climate_control.v0.8.jsonl"
    "prompt_rest.v0.8.jsonl"              = "features\rest_banner.v0.8.jsonl"
    "list_template.v0.8.jsonl"            = "features\list_template.v0.8.jsonl"

    # demos/ — 综合大卡（全控件台架 / 覆盖巡检 / 登录还原）
    "00_full_control_center.v0.8.jsonl"   = "demos\full_control_center.v0.8.jsonl"
    "01_figma_button_demo.v0.8.jsonl"     = "demos\figma_button_demo.v0.8.jsonl"
    "coverage_tour.v0.8.jsonl"            = "demos\coverage_tour.v0.8.jsonl"
    "catalog_all.v0.8.jsonl"              = "demos\catalog_all.v0.8.jsonl"

    # edge/ — 协议边界与降级
    "degrade_unknown.v0.8.jsonl"          = "edge\unknown_type.v0.8.jsonl"
    "invalid_bad_packet.v0.8.jsonl"       = "edge\bad_packet.v0.8.jsonl"
}

# ---- 2. 删除重复样本 ----
$deletes = @("media_card.v0.8.jsonl")

New-Item -ItemType Directory -Force -Path (
    "$base\scenarios", "$base\features", "$base\demos", "$base\edge"
) | Out-Null

foreach ($d in $deletes) {
    $src = Join-Path $base $d
    if (Test-Path $src) {
        Remove-Item $src -Force
        $meta = $src + ".meta"
        if (Test-Path $meta) { Remove-Item $meta -Force }
        Write-Host ("DELETED: " + $d)
    }
}

foreach ($kv in $renames.GetEnumerator()) {
    if ($kv.Value -eq $null) { continue }
    $src = Join-Path $base $kv.Key
    $dst = Join-Path $base $kv.Value
    if (Test-Path $src) {
        $dstDir = Split-Path $dst -Parent
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
        Move-Item $src $dst -Force
        $srcMeta = $src + ".meta"
        $dstMeta = $dst + ".meta"
        if (Test-Path $srcMeta) { Move-Item $srcMeta $dstMeta -Force }
        Write-Host ("RENAMED: " + $kv.Key + " -> " + $kv.Value)
    } else {
        Write-Host ("SKIP (missing): " + $kv.Key)
    }
}

# ---- 3. components/ 单元测重命名（PascalCase → snake_case 统一）----
$compDir = Join-Path $base "components"
Get-ChildItem $compDir -Filter "*.v0.8.jsonl" | ForEach-Object {
    $newName = $_.Name -replace "([a-z])([A-Z])", '$1_$2' -replace "([A-Z]+)([A-Z][a-z])", '$1_$2'
    $newName = $newName.ToLower()
    if ($_.Name -ne $newName) {
        Rename-Item $_.FullName $newName
        Rename-Item ($_.FullName + ".meta") ($newName + ".meta") -ErrorAction SilentlyContinue
        Write-Host ("COMP RENAMED: " + $_.Name + " -> " + $newName)
    }
}

Write-Host "`n=== FINAL SAMPLE TREE ==="
Get-ChildItem $base -Recurse -Filter "*.jsonl" | ForEach-Object {
    $_.FullName.Replace($base + "\", "")
}
