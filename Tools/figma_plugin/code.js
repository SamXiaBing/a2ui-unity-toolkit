// A2UI SchemeA Figma 插件（双命令）
//   build  : 在 MyTest 里画一块真实座舱组件板（≥11 类组件，深色 DS 配色）
//   export : 把选中节点序列化为 A2UI 转换链可用的 JSON（交给 figma_to_uss.py）
//
// 设计原则：插件只产出「Figma 节点 JSON / 真实设计稿」，USS 转换交给 Scheme A 的
// 本地 Python 工具。Figma 只喂 令牌/结构，不把运行时组件树写死。

// ---------------- 导出逻辑 ----------------
function hexOf(c) {
  if (!c) return null;
  return { r: Math.round(c.r * 255), g: Math.round(c.g * 255), b: Math.round(c.b * 255) };
}

function serialize(node) {
  const o = { id: node.id, name: node.name, type: node.type };
  if (node.fills) {
    o.fills = node.fills
      .filter((f) => f.type === "SOLID" && f.visible !== false)
      .map((f) => ({ type: "SOLID", color: hexOf(f.color), opacity: f.opacity == null ? 1 : f.opacity }));
  }
  if (typeof node.cornerRadius === "number") o.cornerRadius = node.cornerRadius;
  if (node.strokes && node.strokes.length) {
    o.strokes = node.strokes.filter((s) => s.type === "SOLID").map((s) => ({ type: "SOLID", color: hexOf(s.color) }));
  }
  if (node.type === "TEXT") {
    o.characters = node.characters;
    o.style = {
      fontSize: node.fontSize,
      fontWeight: node.fontWeight ? node.fontWeight : null,
      textAlignHorizontal: node.textAlignHorizontal,
    };
  }
  if (node.children && node.children.length) o.children = node.children.map(serialize);
  return o;
}

function buildPayload(rootNodes) {
  return {
    nodes: rootNodes.map((n) => ({ document: serialize(n) })),
    exportedAt: new Date().toISOString(),
    source: "A2UI SchemeA plugin",
  };
}

function summarize(doc) {
  let colors = 0, radii = 0, texts = 0, frames = 0;
  (function walk(n) {
    if (!n) return;
    if (n.fills) colors += n.fills.length;
    if (typeof n.cornerRadius === "number") radii++;
    if (n.type === "TEXT") texts++;
    if (n.type === "FRAME" || n.type === "INSTANCE" || n.type === "COMPONENT") frames++;
    (n.children || []).forEach(walk);
  })(doc);
  return { colors, radii, texts, frames };
}

function runExport(roots) {
  const payload = buildPayload(roots);
  const summary = summarize(payload.nodes[0] ? payload.nodes[0].document : null);
  parent.postMessage({ pluginMessage: { type: "result", payload, summary } }, "*");
}

function startExport() {
  figma.showUI(__html__, { width: 380, height: 360 });
  figma.ui.postMessage({ pluginMessage: { type: "ready" } });

  const exportSelection = () => {
    const sel = figma.currentPage.selection;
    if (sel.length) runExport(sel);
    else figma.ui.postMessage({ pluginMessage: { type: "empty" } });
  };

  figma.on("selectionchange", exportSelection);
  figma.ui.onmessage = (msg) => {
    if (msg.type === "export-selection") exportSelection();
    else if (msg.type === "export-page") runExport(figma.currentPage.children);
  };
}

// ---------------- 构建板逻辑 ----------------
const C = {
  bg:           { r: 14,  g: 17,  b: 22  },
  surface:      { r: 20,  g: 24,  b: 33  },
  surfaceVar:   { r: 28,  g: 34,  b: 48  },
  primary:      { r: 45,  g: 212, b: 191 },
  secondary:    { r: 91,  g: 141, b: 239 },
  tertiary:     { r: 245, g: 166, b: 35  },
  onSurface:    { r: 230, g: 237, b: 243 },
  onSurfaceVar: { r: 154, g: 167, b: 184 },
  error:        { r: 255, g: 90,  b: 95  },
  onPrimary:    { r: 6,   g: 35,  b: 31  },
};
const rgb = (c) => ({ r: c.r / 255, g: c.g / 255, b: c.b / 255 });
const fill = (n, c, opacity) => {
  const f = { type: "SOLID", color: rgb(c) };
  if (opacity != null) f.opacity = opacity;
  n.fills = [f];
};

async function makeText(str, size, color) {
  try { await figma.loadFontAsync({ family: "Roboto", style: "Regular" }); } catch (e) {}
  const t = figma.createText();
  t.fontSize = size;
  t.characters = str;
  fill(t, color);
  return t;
}

function makeRect(w, h, c, radius) {
  const r = figma.createRectangle();
  r.resize(w, h);
  fill(r, c);
  if (radius != null) r.cornerRadius = radius;
  return r;
}

async function buildCabinBoard() {
  const page = figma.currentPage;
  const board = figma.createFrame();
  board.name = "Cabin Board";
  board.resize(1080, 2200);
  fill(board, C.bg);
  board.layoutMode = "VERTICAL";
  board.primaryAxisSizingMode = "AUTO";
  board.counterAxisSizingMode = "AUTO";
  board.itemSpacing = 24;
  board.paddingLeft = 60; board.paddingRight = 60;
  board.paddingTop = 60; board.paddingBottom = 60;
  page.appendChild(board);

  const FULL = 960;

  // 1) 标题 + 副标题（TEXT）
  const title = await makeText("Cabin Control Center", 56, C.onSurface);
  title.textAutoResize = "WIDTH_AND_HEIGHT";
  title.resize(FULL, title.height);
  board.appendChild(title);

  const sub = await makeText("控制空调、媒体与驾驶模式", 22, C.onSurfaceVar);
  sub.resize(FULL, sub.height);
  board.appendChild(sub);

  // 2) 卡片（FRAME + TEXT）
  const card = figma.createFrame();
  card.name = "Card";
  fill(card, C.surfaceVar);
  card.cornerRadius = 16;
  card.layoutMode = "VERTICAL";
  card.itemSpacing = 10;
  card.paddingTop = 24; card.paddingBottom = 24; card.paddingLeft = 24; card.paddingRight = 24;
  const cTitle = await makeText("当前温度 22°C", 30, C.onSurface);
  const cBody = await makeText("主驾区 · 风量 2 档", 20, C.onSurfaceVar);
  card.appendChild(cTitle); card.appendChild(cBody);
  board.appendChild(card);

  // 3) 主按钮 + 4) 次按钮（RECT + TEXT）
  const btnPrimary = makeRect(320, 72, C.primary, 12);
  const btnPText = await makeText("▶ 播放音乐", 24, C.onPrimary);
  const pWrap = figma.createFrame(); pWrap.name = "PrimaryButton";
  pWrap.layoutMode = "HORIZONTAL"; pWrap.primaryAxisAlignItems = "CENTER"; pWrap.counterAxisAlignItems = "CENTER";
  pWrap.resize(320, 72); fill(pWrap, C.primary); pWrap.cornerRadius = 12;
  pWrap.appendChild(btnPText);
  board.appendChild(pWrap);

  const sWrap = figma.createFrame(); sWrap.name = "SecondaryButton";
  sWrap.layoutMode = "HORIZONTAL"; sWrap.primaryAxisAlignItems = "CENTER"; sWrap.counterAxisAlignItems = "CENTER";
  sWrap.resize(320, 72); fill(sWrap, C.secondary); sWrap.cornerRadius = 12;
  sWrap.appendChild(await makeText("导航回家", 24, C.onPrimary));
  board.appendChild(sWrap);

  // 5) 滑块（滑块条 + 拖块）
  const track = figma.createFrame(); track.name = "Slider";
  track.layoutMode = "HORIZONTAL"; track.counterAxisAlignItems = "CENTER";
  track.resize(FULL, 8); fill(track, C.surfaceVar); track.cornerRadius = 999;
  const drag = makeRect(56, 56, C.primary, 999);
  drag.x = 480; drag.y = -24;
  track.appendChild(drag);
  board.appendChild(track);

  // 6) 勾选框（RECT + TEXT）
  const chk = figma.createFrame(); chk.name = "CheckBox";
  chk.layoutMode = "HORIZONTAL"; chk.counterAxisAlignItems = "CENTER"; chk.itemSpacing = 16;
  const box = makeRect(40, 40, C.primary, 8);
  const chkLabel = await makeText("自动除雾", 22, C.onSurface);
  chk.appendChild(box); chk.appendChild(chkLabel);
  board.appendChild(chk);

  // 7) 标签条 Chips（多个 pill RECT）
  const chips = figma.createFrame(); chips.name = "Chips";
  chips.layoutMode = "HORIZONTAL"; chips.itemSpacing = 12; chips.counterAxisSizingMode = "AUTO";
  ["运动", "经济", "舒适"].forEach(async (t) => {
    const chip = makeRect(140, 48, C.surfaceVar, 999);
    const ct = await makeText(t, 20, C.onSurface);
    const cw = figma.createFrame(); cw.layoutMode = "HORIZONTAL";
    cw.primaryAxisAlignItems = "CENTER"; cw.counterAxisAlignItems = "CENTER";
    cw.resize(140, 48); fill(cw, C.surfaceVar); cw.cornerRadius = 999;
    cw.appendChild(ct); chips.appendChild(cw);
  });
  board.appendChild(chips);

  // 8) 选项卡 Tabs（多个 pill TEXT）
  const tabs = figma.createFrame(); tabs.name = "Tabs";
  tabs.layoutMode = "HORIZONTAL"; tabs.itemSpacing = 12; tabs.counterAxisSizingMode = "AUTO";
  ["媒体", "车辆", "地图"].forEach(async (t) => {
    const tw = figma.createFrame(); tw.layoutMode = "HORIZONTAL";
    tw.primaryAxisAlignItems = "CENTER"; tw.counterAxisAlignItems = "CENTER";
    tw.resize(160, 48); fill(tw, C.surfaceVar); tw.cornerRadius = 999;
    tw.appendChild(await makeText(t, 20, C.onSurfaceVar));
    tabs.appendChild(tw);
  });
  board.appendChild(tabs);

  // 9) 分割线（RECT）
  const div = makeRect(FULL, 1, C.onSurfaceVar);
  board.appendChild(div);

  // 10) 文本输入（RECT + TEXT）
  const field = figma.createFrame(); field.name = "TextField";
  field.layoutMode = "HORIZONTAL"; field.counterAxisAlignItems = "CENTER";
  field.resize(FULL, 64); fill(field, C.surface); field.cornerRadius = 8;
  field.paddingLeft = 20;
  field.appendChild(await makeText("说点什么…", 20, C.onSurfaceVar));
  board.appendChild(field);

  // 11) 图片占位（RECT）
  const img = makeRect(FULL, 220, C.secondary, 16);
  board.appendChild(img);

  // 12) 图标（TEXT glyph）
  const icon = await makeText("⚙", 48, C.onSurfaceVar);
  board.appendChild(icon);

  // 13) 座舱媒体条（ROW: 图标 + 文本 + 按钮）
  const bar = figma.createFrame(); bar.name = "CabinMediaBar";
  bar.layoutMode = "HORIZONTAL"; bar.counterAxisAlignItems = "CENTER"; bar.itemSpacing = 20;
  bar.resize(FULL, 96); fill(bar, C.surface); bar.cornerRadius = 16;
  bar.paddingLeft = 24; bar.paddingRight = 24;
  const bIcon = await makeText("♪", 40, C.primary);
  const bText = await makeText("正在播放 · 电台 1", 22, C.onSurface);
  const bBtn = makeRect(140, 56, C.primary, 999);
  bar.appendChild(bIcon); bar.appendChild(bText); bar.appendChild(bBtn);
  board.appendChild(bar);

  figma.currentPage.selection = [board];
  figma.viewport.scrollAndZoomIntoView([board]);
  figma.closePlugin("已生成 Cabin Board（13 类组件）");
}

// ---------------- 入口 ----------------
if (figma.command === "build") {
  buildCabinBoard();
} else {
  startExport();
}
