/*
 * Board renderer for the structured BoardAction schema.
 *
 * Consumes an array of action objects (see Models/Board/BoardAction.cs)
 * and paints them onto a canvas with a handwritten-whiteboard aesthetic.
 *
 * This module is intentionally self-contained: it does not depend on the
 * legacy site.js pipeline. It uses rough.js when available for sketchy
 * strokes, and falls back to plain canvas strokes when it isn't.
 */
(function (global) {
  "use strict";

  const PALETTE = {
    ink:       "#1f2937",
    chalk:     "#e5e7eb",
    accent:    "#7c3aed",
    highlight: "#facc15",
    danger:    "#ef4444",
    success:   "#16a34a",
    muted:     "#6b7280"
  };

  const SIZES = {
    xs:    16,
    sm:    20,
    md:    26,
    lg:    32,
    xl:    40,
    title: 52
  };

  const HAND_FONT = "\"Kalam\", \"Caveat\", \"Patrick Hand\", \"Comic Sans MS\", cursive";

  // Named regions -> (x, y, w, h) fraction of the canvas.
  const REGIONS = {
    "TopLeft":      { fx: 0.04, fy: 0.06, fw: 0.30, fh: 0.20 },
    "TopCenter":    { fx: 0.34, fy: 0.06, fw: 0.32, fh: 0.20 },
    "TopRight":     { fx: 0.66, fy: 0.06, fw: 0.30, fh: 0.20 },
    "Left":         { fx: 0.04, fy: 0.28, fw: 0.30, fh: 0.44 },
    "Center":       { fx: 0.34, fy: 0.28, fw: 0.32, fh: 0.44 },
    "Right":        { fx: 0.66, fy: 0.28, fw: 0.30, fh: 0.44 },
    "BottomLeft":   { fx: 0.04, fy: 0.74, fw: 0.30, fh: 0.22 },
    "BottomCenter": { fx: 0.34, fy: 0.74, fw: 0.32, fh: 0.22 },
    "BottomRight":  { fx: 0.66, fy: 0.74, fw: 0.30, fh: 0.22 },
    "WorkArea":     { fx: 0.32, fy: 0.20, fw: 0.44, fh: 0.60 },
    "Sidebar":      { fx: 0.78, fy: 0.10, fw: 0.20, fh: 0.80 },
    "AnswerBox":    { fx: 0.34, fy: 0.78, fw: 0.32, fh: 0.16 },
    "Title":        { fx: 0.04, fy: 0.02, fw: 0.92, fh: 0.08 }
  };

  function regionRect(canvas, region) {
    const key = region || "Center";
    const spec = REGIONS[key] || REGIONS["Center"];
    return {
      x: Math.round(canvas.width  * spec.fx),
      y: Math.round(canvas.height * spec.fy),
      w: Math.round(canvas.width  * spec.fw),
      h: Math.round(canvas.height * spec.fh)
    };
  }

  function colorFor(style, fallback) {
    if (!style || !style.color) return fallback || PALETTE.ink;
    return PALETTE[style.color] || fallback || PALETTE.ink;
  }

  function sizeFor(style, fallback) {
    if (!style || !style.size) return fallback || SIZES.md;
    return SIZES[style.size] || fallback || SIZES.md;
  }

  function makeRough(canvas) {
    if (global.rough && typeof global.rough.canvas === "function") {
      try { return global.rough.canvas(canvas); } catch (_) { return null; }
    }
    return null;
  }

  // ---------- Region-flow layout ----------
  //
  // Actions that don't declare a region get placed automatically:
  // text/math flow top-to-bottom in the left column, geometry goes to WorkArea,
  // and emphasis actions inherit the region of their target.
  function autoRegion(action) {
    switch (action.type) {
      case "write_text":
      case "write_math":
        return "Left";
      case "draw_axes":
      case "draw_line":
      case "draw_point":
      case "draw_circle":
      case "draw_square":
      case "draw_triangle":
      case "draw_arrow":
      case "draw_bar_chart":
      case "draw_bracket":
        return "WorkArea";
      default:
        return "Center";
    }
  }

  // ---------- Rendering pipeline ----------

  function BoardRenderer(canvas, opts) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.rough = makeRough(canvas);
    this.opts = Object.assign({
      background: "#fafaf7",
      grid: false,
      showLog: false
    }, opts || {});

    this.reset();
  }

  BoardRenderer.prototype.reset = function () {
    this.state = {
      actions: [],
      lastIndex: -1,       // highest fully-executed action
      pageActions: [],     // actions on the current page
      textFlow: {},        // region -> next y baseline
      axesById: {},        // axes registry for line/point references
      pageIndex: 0,
      log: []              // debug log of executed actions
    };
    this.repaint();
  };

  BoardRenderer.prototype.setActions = function (actions) {
    this.state.actions = Array.isArray(actions) ? actions.slice() : [];
    this.state.lastIndex = -1;
    this.state.pageActions = [];
    this.state.textFlow = {};
    this.state.axesById = {};
    this.state.log = [];
    this.state.pageIndex = 0;
    this.repaint();
  };

  // Advance execution up to (and including) action index `upto`.
  BoardRenderer.prototype.advanceTo = function (upto) {
    if (upto < this.state.lastIndex) {
      // Going backwards: replay from scratch (cheap enough for hundreds of actions).
      const target = upto;
      this.state.lastIndex = -1;
      this.state.pageActions = [];
      this.state.textFlow = {};
      this.state.axesById = {};
      this.state.log = [];
      this.state.pageIndex = 0;
      this.repaint();
      for (let i = 0; i <= target; i++) this.executeAction(i);
      this.state.lastIndex = target;
      this.repaint();
      return;
    }
    for (let i = this.state.lastIndex + 1; i <= upto; i++)
      this.executeAction(i);
    this.state.lastIndex = upto;
    this.repaint();
  };

  BoardRenderer.prototype.executeAction = function (i) {
    const action = this.state.actions[i];
    if (!action) return;
    const record = Object.assign({}, action, { _index: i });

    // Handle flow-control actions before adding to page.
    if (action.type === "clear") {
      this.state.pageActions = [];
      this.state.textFlow = {};
      this.state.log.push({ i, type: action.type });
      return;
    }
    if (action.type === "new_page") {
      this.state.pageActions = [];
      this.state.textFlow = {};
      this.state.axesById = {};
      this.state.pageIndex++;
      this.state.log.push({ i, type: action.type });
      return;
    }
    if (action.type === "erase") {
      this.state.pageActions = this.state.pageActions.filter(a => a.id !== action.targetId);
      this.state.log.push({ i, type: action.type, targetId: action.targetId });
      return;
    }

    // Assign an effective region if the model didn't set one.
    if (!record.region) record.region = autoRegion(record);

    // Register axes by id so later line/point actions can reference them.
    if (record.type === "draw_axes" && record.id)
      this.state.axesById[record.id] = record;

    this.state.pageActions.push(record);
    this.state.log.push({ i, type: action.type, id: action.id, region: record.region });
  };

  BoardRenderer.prototype.repaint = function () {
    const c = this.ctx;
    const cv = this.canvas;

    // Clear + background.
    c.save();
    c.fillStyle = this.opts.background;
    c.fillRect(0, 0, cv.width, cv.height);

    // Faint whiteboard grain.
    c.fillStyle = "rgba(0,0,0,0.02)";
    for (let y = 0; y < cv.height; y += 3) c.fillRect(0, y, cv.width, 1);
    c.restore();

    // Redraw everything on the current page in order.
    // Text flow is recomputed per-repaint so wrapping / new lines land correctly.
    this.state.textFlow = {};
    for (const action of this.state.pageActions) this.drawAction(action);
  };

  BoardRenderer.prototype.drawAction = function (action) {
    switch (action.type) {
      case "write_text":     return this.drawText(action);
      case "write_math":     return this.drawText({ ...action, text: action.latex });
      case "draw_axes":      return this.drawAxes(action);
      case "draw_line":      return this.drawLine(action);
      case "draw_point":     return this.drawPoint(action);
      case "draw_circle":    return this.drawCircle(action);
      case "draw_square":    return this.drawSquare(action);
      case "draw_triangle":  return this.drawTriangle(action);
      case "draw_arrow":     return this.drawArrow(action);
      case "draw_bracket":   return this.drawBracket(action);
      case "draw_bar_chart": return this.drawBarChart(action);
      case "highlight":      return this.drawHighlight(action);
      case "circle_term":    return this.drawCircleTerm(action);
      case "underline":      return this.drawUnderline(action);
      case "strike":         return this.drawStrike(action);
      case "focus":          return this.drawFocus(action);
      default: return;
    }
  };

  BoardRenderer.prototype.textMetrics = function (action) {
    const size = sizeFor(action.style, SIZES.md);
    const weight = action.style && action.style.emphasis ? "700" : "400";
    return { size, weight };
  };

  BoardRenderer.prototype.drawText = function (action) {
    const rect = regionRect(this.canvas, action.region);
    const { size, weight } = this.textMetrics(action);
    const color = colorFor(action.style, PALETTE.ink);

    const key = action.region || "Center";
    if (this.state.textFlow[key] === undefined) this.state.textFlow[key] = rect.y + size;
    const y = this.state.textFlow[key];
    this.state.textFlow[key] += size + 8;

    const c = this.ctx;
    c.save();
    c.font = `${weight} ${size}px ${HAND_FONT}`;
    c.fillStyle = color;
    c.textBaseline = "alphabetic";
    // rotate a hair for a hand-written feel
    c.translate(rect.x + 6, y);
    c.rotate((Math.random() - 0.5) * 0.005);
    c.fillText(action.text || "", 0, 0);
    // Remember bounds for later emphasis actions.
    action._bounds = {
      x: rect.x + 6,
      y: y - size,
      w: c.measureText(action.text || "").width,
      h: size + 4
    };
    c.restore();
  };

  BoardRenderer.prototype.axesMapper = function (rect, range) {
    const pad = 24;
    const usableW = rect.w - pad * 2;
    const usableH = rect.h - pad * 2;
    const x0 = rect.x + pad;
    const y0 = rect.y + pad;
    const xSpan = range.xMax - range.xMin;
    const ySpan = range.yMax - range.yMin;
    return {
      mapX: (x) => x0 + ((x - range.xMin) / xSpan) * usableW,
      mapY: (y) => y0 + usableH - ((y - range.yMin) / ySpan) * usableH,
      rect,
      range,
      bounds: { left: x0, right: x0 + usableW, top: y0, bottom: y0 + usableH }
    };
  };

  BoardRenderer.prototype.drawAxes = function (action) {
    const rect = regionRect(this.canvas, action.region);
    const range = action.range || { xMin: -5, xMax: 5, yMin: -5, yMax: 5 };
    const map = this.axesMapper(rect, range);
    action._mapper = map;

    const c = this.ctx;
    c.save();
    c.strokeStyle = "rgba(31,41,55,0.30)";
    c.lineWidth = 1;
    // Grid.
    for (let x = Math.ceil(range.xMin); x <= Math.floor(range.xMax); x++) {
      const gx = map.mapX(x);
      c.beginPath(); c.moveTo(gx, map.bounds.top); c.lineTo(gx, map.bounds.bottom); c.stroke();
    }
    for (let y = Math.ceil(range.yMin); y <= Math.floor(range.yMax); y++) {
      const gy = map.mapY(y);
      c.beginPath(); c.moveTo(map.bounds.left, gy); c.lineTo(map.bounds.right, gy); c.stroke();
    }
    // Axes.
    c.strokeStyle = PALETTE.ink;
    c.lineWidth = 2;
    const zx = map.mapX(0);
    const zy = map.mapY(0);
    if (zx >= map.bounds.left && zx <= map.bounds.right) {
      c.beginPath(); c.moveTo(zx, map.bounds.top); c.lineTo(zx, map.bounds.bottom); c.stroke();
    }
    if (zy >= map.bounds.top && zy <= map.bounds.bottom) {
      c.beginPath(); c.moveTo(map.bounds.left, zy); c.lineTo(map.bounds.right, zy); c.stroke();
    }
    // Labels.
    c.fillStyle = PALETTE.muted;
    c.font = `400 14px ${HAND_FONT}`;
    c.textBaseline = "alphabetic";
    c.fillText(action.xLabel || "x", map.bounds.right + 4, zy + 5);
    c.fillText(action.yLabel || "y", zx + 4, map.bounds.top - 2);
    c.restore();
  };

  BoardRenderer.prototype.findAxes = function (action) {
    if (action.axesId && this.state.axesById[action.axesId])
      return this.state.axesById[action.axesId]._mapper;
    // fall back to most recent axes on the page
    for (let i = this.state.pageActions.length - 1; i >= 0; i--) {
      const a = this.state.pageActions[i];
      if (a.type === "draw_axes" && a._mapper) return a._mapper;
    }
    return null;
  };

  BoardRenderer.prototype.parseLineExpr = function (eq) {
    if (!eq) return null;
    const s = String(eq).replace(/\s+/g, "").toLowerCase();
    if (s.startsWith("y=")) {
      const rhs = s.slice(2);
      if (rhs.includes("x")) {
        const parts = rhs.split("x");
        let m = 1;
        if (parts[0] === "-") m = -1;
        else if (parts[0] !== "" && parts[0] !== "+") m = parseFloat(parts[0]);
        let b = 0;
        if (parts.length > 1 && parts[1]) b = parseFloat(parts[1]) || 0;
        return { kind: "sl", m, b };
      }
      return { kind: "sl", m: 0, b: parseFloat(rhs) || 0 };
    }
    if (s.startsWith("x=")) return { kind: "vert", x: parseFloat(s.slice(2)) || 0 };
    return null;
  };

  BoardRenderer.prototype.drawLine = function (action) {
    const map = this.findAxes(action);
    if (!map) return;
    const expr = this.parseLineExpr(action.equation);
    if (!expr) return;
    const color = colorFor(action.style, PALETTE.accent);
    const c = this.ctx;

    c.save();
    c.strokeStyle = color;
    c.lineWidth = 3;
    c.lineCap = "round";
    if (expr.kind === "vert") {
      const x = map.mapX(expr.x);
      c.beginPath(); c.moveTo(x, map.bounds.top); c.lineTo(x, map.bounds.bottom); c.stroke();
    } else {
      const x1 = map.range.xMin;
      const x2 = map.range.xMax;
      const y1 = expr.m * x1 + expr.b;
      const y2 = expr.m * x2 + expr.b;
      this.strokeSegmentSketchy(c, map.mapX(x1), map.mapY(y1), map.mapX(x2), map.mapY(y2));
    }
    if (action.label) {
      c.fillStyle = color;
      c.font = `400 16px ${HAND_FONT}`;
      c.fillText(action.label, map.bounds.right - 60, map.bounds.top + 20);
    }
    c.restore();
  };

  BoardRenderer.prototype.strokeSegmentSketchy = function (c, x1, y1, x2, y2) {
    if (this.rough) {
      this.rough.line(x1, y1, x2, y2, { roughness: 1.2, stroke: c.strokeStyle, strokeWidth: 3 });
      return;
    }
    c.beginPath(); c.moveTo(x1, y1); c.lineTo(x2, y2); c.stroke();
  };

  BoardRenderer.prototype.drawPoint = function (action) {
    const map = this.findAxes(action);
    if (!map || !action.at) return;
    const c = this.ctx;
    const px = map.mapX(action.at.x);
    const py = map.mapY(action.at.y);
    c.save();
    c.fillStyle = colorFor(action.style, PALETTE.danger);
    c.beginPath(); c.arc(px, py, 5, 0, Math.PI * 2); c.fill();
    if (action.label) {
      c.font = `400 16px ${HAND_FONT}`;
      c.fillStyle = PALETTE.ink;
      c.fillText(action.label, px + 8, py - 8);
    }
    c.restore();
  };

  BoardRenderer.prototype.drawCircle = function (action) {
    const map = this.findAxes(action);
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const c = this.ctx;
    let cx, cy, r;
    if (map && action.center) {
      cx = map.mapX(action.center.x);
      cy = map.mapY(action.center.y);
      const cxr = map.mapX(action.center.x + action.radius);
      r = Math.abs(cxr - cx);
    } else {
      cx = rect.x + rect.w / 2;
      cy = rect.y + rect.h / 2;
      r  = Math.min(rect.w, rect.h) * 0.30;
    }
    c.save();
    if (this.rough) {
      this.rough.circle(cx, cy, r * 2, { roughness: 1.2, stroke: colorFor(action.style, PALETTE.accent), strokeWidth: 3 });
    } else {
      c.strokeStyle = colorFor(action.style, PALETTE.accent);
      c.lineWidth = 3;
      c.beginPath(); c.arc(cx, cy, r, 0, Math.PI * 2); c.stroke();
    }
    if (action.label) {
      c.font = `400 16px ${HAND_FONT}`;
      c.fillStyle = PALETTE.ink;
      c.fillText(action.label, cx + r + 6, cy);
    }
    c.restore();
  };

  BoardRenderer.prototype.drawSquare = function (action) {
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const c = this.ctx;
    const scale = Math.min(rect.w, rect.h) / 8;  // pretend each unit ~= 1/8 of region
    const size = (action.size || 2) * scale;
    const cx = rect.x + rect.w / 2;
    const cy = rect.y + rect.h / 2;
    const angle = ((action.angleDegrees || 0) * Math.PI) / 180;
    c.save();
    c.translate(cx, cy);
    c.rotate(angle);
    if (this.rough) {
      this.rough.rectangle(-size / 2, -size / 2, size, size, { roughness: 1.2, stroke: colorFor(action.style, PALETTE.accent), strokeWidth: 3 });
    } else {
      c.strokeStyle = colorFor(action.style, PALETTE.accent);
      c.lineWidth = 3;
      c.strokeRect(-size / 2, -size / 2, size, size);
    }
    c.restore();
  };

  BoardRenderer.prototype.drawTriangle = function (action) {
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const c = this.ctx;
    // Vertices in local (region-relative) coords.
    let v1, v2, v3;
    if (action.v1 && action.v2 && action.v3) {
      const scale = Math.min(rect.w, rect.h) / 8;
      const originX = rect.x + rect.w / 2;
      const originY = rect.y + rect.h / 2;
      v1 = { x: originX + action.v1.x * scale, y: originY - action.v1.y * scale };
      v2 = { x: originX + action.v2.x * scale, y: originY - action.v2.y * scale };
      v3 = { x: originX + action.v3.x * scale, y: originY - action.v3.y * scale };
    } else {
      const base = action.baseLength || 4;
      const height = action.heightLength ||
        (action.acuteAngleDegrees ? base * Math.tan((action.acuteAngleDegrees * Math.PI) / 180) : 3);
      const maxDim = Math.max(base, height);
      const scale = (Math.min(rect.w, rect.h) * 0.7) / maxDim;
      const bx = rect.x + rect.w * 0.20;
      const by = rect.y + rect.h * 0.80;
      v1 = { x: bx, y: by };
      v2 = { x: bx + base * scale, y: by };
      v3 = { x: bx, y: by - height * scale };
    }
    c.save();
    const color = colorFor(action.style, PALETTE.accent);
    if (this.rough) {
      this.rough.polygon(
        [[v1.x, v1.y], [v2.x, v2.y], [v3.x, v3.y]],
        { roughness: 1.2, stroke: color, strokeWidth: 3 }
      );
    } else {
      c.strokeStyle = color;
      c.lineWidth = 3;
      c.beginPath();
      c.moveTo(v1.x, v1.y); c.lineTo(v2.x, v2.y); c.lineTo(v3.x, v3.y); c.closePath();
      c.stroke();
    }
    // Right-angle box if base and height are perpendicular (common case).
    if (!action.v1) {
      c.strokeStyle = color;
      c.lineWidth = 2;
      const bs = 12;
      c.strokeRect(v1.x, v1.y - bs, bs, bs);
    }
    // Labels.
    const lab = action.labels || {};
    c.font = `400 16px ${HAND_FONT}`;
    c.fillStyle = PALETTE.ink;
    if (lab.baseLabel)       c.fillText(lab.baseLabel,       (v1.x + v2.x) / 2, v1.y + 20);
    if (lab.heightLabel)     c.fillText(lab.heightLabel,     v1.x - 24,          (v1.y + v3.y) / 2);
    if (lab.hypotenuseLabel) c.fillText(lab.hypotenuseLabel, (v2.x + v3.x) / 2 + 6, (v2.y + v3.y) / 2 - 6);
    if (lab.angleLabel)      c.fillText(lab.angleLabel,      v2.x - 28, v2.y - 8);
    c.restore();
  };

  BoardRenderer.prototype.drawArrow = function (action) {
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const c = this.ctx;
    // Interpret From/To as fractions of the region if they're small (0-1 range).
    const from = pointInRegion(rect, action.from);
    const to   = pointInRegion(rect, action.to);
    c.save();
    const color = colorFor(action.style, PALETTE.accent);
    c.strokeStyle = color;
    c.fillStyle = color;
    c.lineWidth = 3;
    c.lineCap = "round";
    if (action.shape === "curve") {
      const midX = (from.x + to.x) / 2;
      const midY = Math.min(from.y, to.y) - Math.abs(to.x - from.x) * 0.35;
      c.beginPath();
      c.moveTo(from.x, from.y);
      c.quadraticCurveTo(midX, midY, to.x, to.y);
      c.stroke();
    } else if (this.rough) {
      this.rough.line(from.x, from.y, to.x, to.y, { roughness: 1.2, stroke: color, strokeWidth: 3 });
    } else {
      c.beginPath(); c.moveTo(from.x, from.y); c.lineTo(to.x, to.y); c.stroke();
    }
    // Arrowhead.
    const angle = Math.atan2(to.y - from.y, to.x - from.x);
    const head = 12;
    c.beginPath();
    c.moveTo(to.x, to.y);
    c.lineTo(to.x - head * Math.cos(angle - 0.4), to.y - head * Math.sin(angle - 0.4));
    c.lineTo(to.x - head * Math.cos(angle + 0.4), to.y - head * Math.sin(angle + 0.4));
    c.closePath();
    c.fill();
    if (action.label) {
      c.font = `400 15px ${HAND_FONT}`;
      c.fillStyle = PALETTE.ink;
      c.fillText(action.label, (from.x + to.x) / 2, (from.y + to.y) / 2 - 6);
    }
    c.restore();
  };

  function pointInRegion(rect, p) {
    if (!p) return { x: rect.x + rect.w / 2, y: rect.y + rect.h / 2 };
    // Treat |x|,|y| <= 1 as fractional coordinates of the region.
    if (Math.abs(p.x) <= 1 && Math.abs(p.y) <= 1) {
      return { x: rect.x + p.x * rect.w, y: rect.y + p.y * rect.h };
    }
    // Otherwise treat as pixel offsets from region origin.
    return { x: rect.x + p.x, y: rect.y + p.y };
  }

  BoardRenderer.prototype.drawBracket = function (action) {
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const c = this.ctx;
    const from = pointInRegion(rect, action.from);
    const to   = pointInRegion(rect, action.to);
    const midY = (from.y + to.y) / 2;
    const color = colorFor(action.style, PALETTE.ink);
    c.save();
    c.strokeStyle = color; c.lineWidth = 2;
    c.beginPath();
    if ((action.bracketStyle || "curly") === "curly") {
      const tipX = Math.max(from.x, to.x) + 12;
      c.moveTo(from.x, from.y);
      c.quadraticCurveTo(tipX, from.y, tipX, midY);
      c.quadraticCurveTo(tipX, to.y, to.x, to.y);
    } else {
      c.moveTo(from.x, from.y);
      c.lineTo(from.x + 12, from.y);
      c.lineTo(from.x + 12, to.y);
      c.lineTo(to.x, to.y);
    }
    c.stroke();
    if (action.label) {
      c.font = `400 15px ${HAND_FONT}`;
      c.fillStyle = PALETTE.ink;
      c.fillText(action.label, Math.max(from.x, to.x) + 24, midY + 4);
    }
    c.restore();
  };

  BoardRenderer.prototype.drawBarChart = function (action) {
    const rect = regionRect(this.canvas, action.region || "WorkArea");
    const bars = Array.isArray(action.bars) ? action.bars : [];
    if (!bars.length) return;
    const c = this.ctx;
    const pad = 20;
    const usableW = rect.w - pad * 2;
    const usableH = rect.h - pad * 2 - 24; // leave room for labels
    const max = Math.max(...bars.map(b => b.value || 0), 1);
    const bw = usableW / bars.length * 0.7;
    const gap = usableW / bars.length * 0.3;
    c.save();
    c.font = `400 14px ${HAND_FONT}`;
    c.fillStyle = PALETTE.ink;
    if (action.title) c.fillText(action.title, rect.x + pad, rect.y + 16);
    for (let i = 0; i < bars.length; i++) {
      const b = bars[i];
      const bh = ((b.value || 0) / max) * usableH;
      const bx = rect.x + pad + i * (bw + gap) + gap / 2;
      const by = rect.y + rect.h - pad - bh;
      const color = colorFor(action.style, PALETTE.accent);
      if (this.rough) {
        this.rough.rectangle(bx, by, bw, bh, { roughness: 1.0, stroke: color, fill: color, fillStyle: "hachure" });
      } else {
        c.fillStyle = color;
        c.fillRect(bx, by, bw, bh);
      }
      c.fillStyle = PALETTE.ink;
      c.fillText(String(b.label || ""), bx, rect.y + rect.h - 4);
      c.fillText(String(b.value ?? ""), bx, by - 4);
    }
    c.restore();
  };

  BoardRenderer.prototype.findBounds = function (targetId) {
    for (const a of this.state.pageActions) {
      if (a.id === targetId && a._bounds) return a._bounds;
    }
    return null;
  };

  BoardRenderer.prototype.drawHighlight = function (action) {
    const b = this.findBounds(action.targetId);
    if (!b) return;
    const c = this.ctx;
    c.save();
    c.fillStyle = "rgba(250,204,21,0.45)"; // marker yellow
    c.fillRect(b.x - 2, b.y + 2, b.w + 4, b.h - 2);
    c.restore();
  };

  BoardRenderer.prototype.drawCircleTerm = function (action) {
    const b = this.findBounds(action.targetId);
    if (!b) return;
    const c = this.ctx;
    const cx = b.x + b.w / 2;
    const cy = b.y + b.h / 2;
    const rx = b.w / 2 + 8;
    const ry = b.h / 2 + 6;
    c.save();
    if (this.rough) {
      this.rough.ellipse(cx, cy, rx * 2, ry * 2, { roughness: 1.5, stroke: PALETTE.danger, strokeWidth: 2.5 });
    } else {
      c.strokeStyle = PALETTE.danger;
      c.lineWidth = 2.5;
      c.beginPath(); c.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2); c.stroke();
    }
    c.restore();
  };

  BoardRenderer.prototype.drawUnderline = function (action) {
    const b = this.findBounds(action.targetId);
    if (!b) return;
    const c = this.ctx;
    c.save();
    c.strokeStyle = colorFor(action.style, PALETTE.accent);
    c.lineWidth = 2;
    if (this.rough) {
      this.rough.line(b.x, b.y + b.h + 2, b.x + b.w, b.y + b.h + 2, { roughness: 1.2, stroke: c.strokeStyle, strokeWidth: 2 });
    } else {
      c.beginPath(); c.moveTo(b.x, b.y + b.h + 2); c.lineTo(b.x + b.w, b.y + b.h + 2); c.stroke();
    }
    c.restore();
  };

  BoardRenderer.prototype.drawStrike = function (action) {
    const b = this.findBounds(action.targetId);
    if (!b) return;
    const c = this.ctx;
    c.save();
    c.strokeStyle = PALETTE.danger;
    c.lineWidth = 2;
    c.beginPath(); c.moveTo(b.x, b.y + b.h / 2); c.lineTo(b.x + b.w, b.y + b.h / 2); c.stroke();
    c.restore();
  };

  BoardRenderer.prototype.drawFocus = function (action) {
    const b = action.targetId ? this.findBounds(action.targetId) : null;
    if (!b) return;
    const c = this.ctx;
    c.save();
    c.strokeStyle = "rgba(124,58,237,0.8)";
    c.lineWidth = 2;
    c.setLineDash([6, 4]);
    c.strokeRect(b.x - 6, b.y - 6, b.w + 12, b.h + 12);
    c.restore();
  };

  BoardRenderer.prototype.getLog = function () { return this.state.log.slice(); };

  global.BoardRenderer = BoardRenderer;
  global.BoardRendererPalette = PALETTE;
})(window);
