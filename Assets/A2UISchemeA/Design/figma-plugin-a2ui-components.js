// A2UI v0.8 — Figma Plugin: 生成组件清单
// 替换 code.js 全部内容，然后在 Figma Desktop 里 Run

figma.showUI(__html__, { width: 1, height: 1 });

(async () => {
  try {
    figma.notify("🚀 开始创建 A2UI 组件...", { timeout: 2000 });

    // ===== 1. 创建 Page =====
    figma.notify("创建 Page...", { timeout: 1500 });
    const page = figma.createPage();
    page.name = "A2UI Components";
    figma.currentPage = page;

    // ===== 2. 颜色变量 =====
    figma.notify("创建颜色变量...", { timeout: 1500 });
    const col = figma.variables.createVariableCollection("A2UI Colors");
    const modeId = col.modes[0].modeId;
    col.renameMode(modeId, "Token A");

    function makeColor(name, hex) {
      const v = figma.variables.createVariable("color/" + name, col, "COLOR");
      v.setValueForMode(modeId, {
        r: parseInt(hex.slice(1, 3), 16) / 255,
        g: parseInt(hex.slice(3, 5), 16) / 255,
        b: parseInt(hex.slice(5, 7), 16) / 255
      });
    }

    makeColor("primary", "#6750A4");      makeColor("onPrimary", "#FFFFFF");
    makeColor("surface", "#FFFBFE");      makeColor("onSurface", "#1C1B1F");
    makeColor("surfaceVariant", "#E7E0EC"); makeColor("onSurfaceVariant", "#49454F");
    makeColor("outline", "#79747E");      makeColor("outlineVariant", "#CAC4D0");
    makeColor("error", "#B3261E");

    // ===== 3. 加载字体 =====
    figma.notify("加载字体...", { timeout: 1500 });
    // Figma 内置字体，一定存在
    await figma.loadFontAsync({ family: "Roboto", style: "Regular" });
    await figma.loadFontAsync({ family: "Roboto", style: "Bold" });
    figma.notify("字体加载完成 ✓", { timeout: 1500 });

    // ===== 4. 工具函数 =====
    function rect(name, w, h, hex, radius, parent) {
      const r = figma.createRectangle();
      r.name = name;
      r.resize(w, h);
      if (hex) {
        r.fills = [{ type: "SOLID", color: hex2rgb(hex) }];
      }
      if (radius) r.cornerRadius = radius;
      (parent || page).appendChild(r);
      return r;
    }

    function text(name, content, size, hex, parent) {
      const t = figma.createText();
      t.name = name;
      t.fontName = { family: "Roboto", style: size >= 30 ? "Bold" : "Regular" };
      t.characters = content;
      t.fontSize = size;
      t.fills = [{ type: "SOLID", color: hex2rgb(hex || "#1C1B1F") }];
      (parent || page).appendChild(t);
      return t;
    }

    function frame(name, parent, autoHeight) {
      const f = figma.createFrame();
      f.name = name;
      f.layoutMode = "VERTICAL";
      f.itemSpacing = 16;
      f.paddingTop = 24; f.paddingBottom = 24; f.paddingLeft = 24; f.paddingRight = 24;
      if (autoHeight) {
        f.primaryAxisSizingMode = "AUTO";
      }
      (parent || page).appendChild(f);
      return f;
    }

    function hex2rgb(hex) {
      return {
        r: parseInt(hex.slice(1, 3), 16) / 255,
        g: parseInt(hex.slice(3, 5), 16) / 255,
        b: parseInt(hex.slice(5, 7), 16) / 255
      };
    }

    // ===== 5. 创建组件 =====
    let yPos = 0;
    const CARD_W = 360;

    // --- 1. Card ---
    figma.notify("创建 Card...", { timeout: 1500 });
    {
      const sec = frame("1. Card", null, true);
      sec.resize(CARD_W, 1);
      sec.x = 0; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "1. Card（卡片容器）", 16, "#49454F", sec);

      const card = figma.createFrame();
      card.name = "root / Card";
      card.layoutMode = "VERTICAL";
      card.itemSpacing = 16;
      card.paddingTop = 24; card.paddingBottom = 24; card.paddingLeft = 24; card.paddingRight = 24;
      card.cornerRadius = 16;
      card.fills = [{ type: "SOLID", color: hex2rgb("#FFFBFE") }];
      card.strokes = [{ type: "SOLID", color: hex2rgb("#CAC4D0") }];
      card.strokeWeight = 1;
      sec.appendChild(card);

      const col2 = figma.createFrame();
      col2.name = "col / Column";
      col2.layoutMode = "VERTICAL";
      col2.itemSpacing = 16;
      col2.primaryAxisSizingMode = "AUTO";
      card.appendChild(col2);

      text("title / Text:h2", "夜航星图", 36, "#1C1B1F", col2);
      rect("div / Divider", 280, 1, "#CAC4D0", 0, col2);
      text("body / Text:body", "Hi-Fi · 车载空间音", 20, "#1C1B1F", col2);
    }

    // --- 2. Button ---
    figma.notify("创建 Button...", { timeout: 1500 });
    {
      const sec = frame("2. Button", null, true);
      sec.resize(CARD_W, 1);
      sec.x = CARD_W + 80; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "2. Button（按钮）", 16, "#49454F", sec);

      const btn1 = figma.createFrame();
      btn1.name = "playBtn / Button:primary";
      btn1.layoutMode = "HORIZONTAL";
      btn1.primaryAxisAlignItems = "CENTER";
      btn1.counterAxisAlignItems = "CENTER";
      btn1.paddingTop = 12; btn1.paddingBottom = 12; btn1.paddingLeft = 24; btn1.paddingRight = 24;
      btn1.cornerRadius = 999;
      btn1.fills = [{ type: "SOLID", color: hex2rgb("#6750A4") }];
      sec.appendChild(btn1);
      text("playLabel / Text:body", "去这里", 20, "#FFFFFF", btn1);

      const btn2 = figma.createFrame();
      btn2.name = "cancelBtn / Button";
      btn2.layoutMode = "HORIZONTAL";
      btn2.primaryAxisAlignItems = "CENTER";
      btn2.counterAxisAlignItems = "CENTER";
      btn2.paddingTop = 12; btn2.paddingBottom = 12; btn2.paddingLeft = 24; btn2.paddingRight = 24;
      btn2.cornerRadius = 999;
      btn2.fills = [{ type: "SOLID", color: hex2rgb("#E8DEF8") }];
      sec.appendChild(btn2);
      text("cancelLabel / Text:body", "取消", 20, "#1D192B", btn2);
    }

    // --- 3. Text 层级 ---
    figma.notify("创建 Text 层级...", { timeout: 1500 });
    {
      const sec = frame("3. Text", null, true);
      sec.resize(CARD_W, 1);
      sec.x = (CARD_W + 80) * 2; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "3. Text（文本层级）", 16, "#49454F", sec);

      text("big / Text:h1", "大标题 H1 · 44px", 44, "#1C1B1F", sec);
      text("title / Text:h2", "卡片标题 H2 · 36px", 36, "#1C1B1F", sec);
      text("sub / Text:h3", "区域标题 H3 · 30px", 30, "#1C1B1F", sec);
      text("heading / Text:h4", "子标题 H4 · 24px", 24, "#1C1B1F", sec);
      text("body / Text:body", "正文 Body · 20px", 20, "#1C1B1F", sec);
      text("hint / Text:caption", "辅助 Caption · 15px", 15, "#49454F", sec);
    }

    // 第二行
    yPos = 600;

    // --- 4. Divider + Chip ---
    figma.notify("创建 Divider + Chip...", { timeout: 1500 });
    {
      const sec = frame("4. Divider + Chip", null, true);
      sec.resize(CARD_W, 1);
      sec.x = 0; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "4. Divider / Chip", 16, "#49454F", sec);

      rect("div / Divider", 280, 1, "#CAC4D0", 0, sec);

      const chipRow = figma.createFrame();
      chipRow.name = "chips / Row";
      chipRow.layoutMode = "HORIZONTAL";
      chipRow.itemSpacing = 8;
      chipRow.primaryAxisSizingMode = "AUTO";
      sec.appendChild(chipRow);

      const chip1 = figma.createFrame();
      chip1.name = "chip1 / Chip";
      chip1.layoutMode = "HORIZONTAL";
      chip1.primaryAxisAlignItems = "CENTER";
      chip1.counterAxisAlignItems = "CENTER";
      chip1.paddingTop = 8; chip1.paddingBottom = 8; chip1.paddingLeft = 16; chip1.paddingRight = 16;
      chip1.cornerRadius = 999;
      chip1.fills = [{ type: "SOLID", color: hex2rgb("#E7E0EC") }];
      chipRow.appendChild(chip1);
      text("chip1Label / Text:caption", "未选中", 15, "#1C1B1F", chip1);

      const chip2 = figma.createFrame();
      chip2.name = "chip2 / Chip:checked";
      chip2.layoutMode = "HORIZONTAL";
      chip2.primaryAxisAlignItems = "CENTER";
      chip2.counterAxisAlignItems = "CENTER";
      chip2.paddingTop = 8; chip2.paddingBottom = 8; chip2.paddingLeft = 16; chip2.paddingRight = 16;
      chip2.cornerRadius = 999;
      chip2.fills = [{ type: "SOLID", color: hex2rgb("#6750A4") }];
      chipRow.appendChild(chip2);
      text("chip2Label / Text:caption", "选中态", 15, "#FFFFFF", chip2);
    }

    // --- 5. MediaMiniBar ---
    figma.notify("创建 MediaMiniBar...", { timeout: 1500 });
    {
      const sec = frame("5. MediaMiniBar", null, true);
      sec.resize(CARD_W, 1);
      sec.x = CARD_W + 80; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "5. MediaMiniBar（媒体条）", 16, "#49454F", sec);

      const bar = figma.createFrame();
      bar.name = "bar / MediaMiniBar";
      bar.layoutMode = "HORIZONTAL";
      bar.itemSpacing = 16;
      bar.counterAxisAlignItems = "CENTER";
      bar.paddingTop = 24; bar.paddingBottom = 24; bar.paddingLeft = 24; bar.paddingRight = 24;
      bar.cornerRadius = 16;
      bar.fills = [{ type: "SOLID", color: hex2rgb("#E7E0EC") }];
      sec.appendChild(bar);

      rect("cover / Icon", 72, 72, "#F3EDF7", 12, bar);

      const meta = figma.createFrame();
      meta.name = "meta / Column";
      meta.layoutMode = "VERTICAL";
      meta.itemSpacing = 4;
      meta.primaryAxisSizingMode = "AUTO";
      bar.appendChild(meta);
      text("title / Text:h4", "夜航星图", 24, "#1C1B1F", meta);
      text("status / Text:caption", "正在播放 ▶", 15, "#49454F", meta);

      const playBtn = figma.createFrame();
      playBtn.name = "playBtn / Button:primary";
      playBtn.layoutMode = "HORIZONTAL";
      playBtn.primaryAxisAlignItems = "CENTER";
      playBtn.counterAxisAlignItems = "CENTER";
      playBtn.paddingTop = 12; playBtn.paddingBottom = 12; playBtn.paddingLeft = 16; playBtn.paddingRight = 16;
      playBtn.cornerRadius = 999;
      playBtn.fills = [{ type: "SOLID", color: hex2rgb("#6750A4") }];
      bar.appendChild(playBtn);
      text("playLabel / Text:body", "▶", 20, "#FFFFFF", playBtn);
    }

    // --- 6. MultipleChoice ---
    figma.notify("创建 MultipleChoice...", { timeout: 1500 });
    {
      const sec = frame("6. MultipleChoice", null, true);
      sec.resize(CARD_W, 1);
      sec.x = (CARD_W + 80) * 2; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "6. MultipleChoice（多选）", 16, "#49454F", sec);

      const options = figma.createFrame();
      options.name = "options / Column";
      options.layoutMode = "VERTICAL";
      options.itemSpacing = 12;
      options.primaryAxisSizingMode = "AUTO";
      sec.appendChild(options);

      // opt1 checked
      const opt1 = figma.createFrame();
      opt1.name = "opt1 / Toggle:checked";
      opt1.layoutMode = "HORIZONTAL";
      opt1.itemSpacing = 12;
      opt1.counterAxisAlignItems = "CENTER";
      opt1.primaryAxisSizingMode = "AUTO";
      options.appendChild(opt1);
      rect("chk1Box", 24, 24, "#6750A4", 8, opt1);
      text("opt1Label / Text:body", "优先更快（高速）", 20, "#1C1B1F", opt1);

      // opt2 unchecked
      const opt2 = figma.createFrame();
      opt2.name = "opt2 / Toggle";
      opt2.layoutMode = "HORIZONTAL";
      opt2.itemSpacing = 12;
      opt2.counterAxisAlignItems = "CENTER";
      opt2.primaryAxisSizingMode = "AUTO";
      options.appendChild(opt2);
      const chk2 = rect("chk2Box", 24, 24, "#E7E0EC", 8, opt2);
      chk2.strokes = [{ type: "SOLID", color: hex2rgb("#79747E") }];
      chk2.strokeWeight = 2;
      text("opt2Label / Text:body", "优先少花钱（辅路）", 20, "#1C1B1F", opt2);

      // opt3 unchecked
      const opt3 = figma.createFrame();
      opt3.name = "opt3 / Toggle";
      opt3.layoutMode = "HORIZONTAL";
      opt3.itemSpacing = 12;
      opt3.counterAxisAlignItems = "CENTER";
      opt3.primaryAxisSizingMode = "AUTO";
      options.appendChild(opt3);
      const chk3 = rect("chk3Box", 24, 24, "#E7E0EC", 8, opt3);
      chk3.strokes = [{ type: "SOLID", color: hex2rgb("#79747E") }];
      chk3.strokeWeight = 2;
      text("opt3Label / Text:body", "让我自己看地图", 20, "#1C1B1F", opt3);

      text("limit / Text:caption", "最多选 1 项", 15, "#B3261E", sec);
    }

    // --- 7. TextField + Slider ---
    figma.notify("创建 TextField + Slider...", { timeout: 1500 });
    {
      const sec = frame("7. TextField + Slider", null, true);
      sec.resize(CARD_W, 1);
      sec.x = (CARD_W + 80) * 3; sec.y = yPos;
      sec.fills = [{ type: "SOLID", color: hex2rgb("#F5F5F5") }];
      text("sectionTitle", "7. TextField / Slider", 16, "#49454F", sec);

      const field = figma.createFrame();
      field.name = "field / TextField";
      field.layoutMode = "HORIZONTAL";
      field.counterAxisAlignItems = "CENTER";
      field.paddingTop = 12; field.paddingBottom = 12; field.paddingLeft = 16; field.paddingRight = 16;
      field.cornerRadius = 8;
      field.fills = [{ type: "SOLID", color: hex2rgb("#FFFBFE") }];
      field.strokes = [{ type: "SOLID", color: hex2rgb("#CAC4D0") }];
      field.strokeWeight = 1;
      sec.appendChild(field);
      text("fieldPlaceholder / Text:body", "输入文本...", 20, "#79747E", field);

      const sliderFrame = figma.createFrame();
      sliderFrame.name = "slider / Slider";
      sliderFrame.layoutMode = "HORIZONTAL";
      sliderFrame.counterAxisAlignItems = "CENTER";
      sliderFrame.itemSpacing = 0;
      sliderFrame.resize(280, 20);
      sec.appendChild(sliderFrame);
      rect("sliderTrack", 260, 6, "#E7E0EC", 999, sliderFrame);
    }

    // ===== 完成 =====
    figma.notify("✅ A2UI 组件清单已生成！共 7 个组件区", { timeout: 5000 });
    figma.viewport.scrollAndZoomIntoView(page.children);
    figma.closePlugin();

  } catch (err) {
    figma.notify("❌ 错误: " + err.message, { timeout: 10000 });
    console.error(err);
    figma.closePlugin();
  }
})();
