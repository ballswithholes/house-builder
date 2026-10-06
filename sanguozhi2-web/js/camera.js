'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 俯视相机
   ← View/CameraRig.cs：拖动平移、滚轮 / 双指缩放、点击选取、WASD / 方向键平移、Q/E 旋转
   输入只监听渲染画布（Pointer Events），DOM 界面自然会挡住画布上的操作。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  // Unity：Screen.dpi * 0.08 + 6（以 96 dpi 的 CSS 像素计）
  const DRAG_THRESHOLD = 96 * 0.08 + 6;
  const _ray = new THREE.Raycaster();
  const _ndc = new THREE.Vector2();
  const _plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -0.5); // y = 0.5
  const _hit = new THREE.Vector3();

  class CameraRig {
    constructor(camera, dom) {
      this.camera = camera || SG.Gfx.camera;
      this.cam = this.camera;
      this.dom = dom || (SG.Gfx.renderer && SG.Gfx.renderer.domElement);
      this.target = new THREE.Vector3(56, 0, -50);
      this.desired = this.target.clone();
      this.distance = 70; this.desiredDistance = 70;
      this.minDist = 16; this.maxDist = 130;
      this.pitch = 52; this.yaw = 0; this.desiredYaw = 0;
      this.bounds = { xMin: 0, yMin: 0, xMax: 112, yMax: 100 }; // 地图坐标（Unity x / z）
      this.inputEnabled = true;
      this.onTap = [];

      this._pointers = new Map();     // pointerId → { x, y, button, type }
      this._pressing = false; this._dragging = false;
      this._pressId = -1; this._pressX = 0; this._pressY = 0;
      this._lastGround = null;
      this._lastPinch = -1; this._lastMid = null;
      this._keys = new Set();
      this._handlers = [];
      if (this.dom) this._bind();
      this._applyPose();
    }

    // ------------------------------------------------------------ 公共接口 --
    focusMap(mapX, mapY, dist, instant) { this.focusWorld(SG.U(mapX, 0, mapY), dist, instant); }
    focusWorld(p, dist, instant) {
      if (dist === undefined) dist = -1;
      this.desired.set(p.x, p.y || 0, p.z);
      if (dist > 0) this.desiredDistance = dist;
      if (instant) {
        this._clampDesired();
        this.target.copy(this.desired);
        this.distance = this.desiredDistance = M.clamp(this.desiredDistance, this.minDist, this.maxDist);
        this._applyPose();
      }
    }
    // C# 名称兼容
    focus(p, dist, instant) { this.focusWorld(p, dist, instant); }

    update(dt) {
      if (!(dt >= 0)) dt = 0;
      if (this.inputEnabled) this._keyboard(dt);
      const k = 1 - Math.exp(-dt * 10);
      this._clampDesired();
      this.desiredDistance = M.clamp(this.desiredDistance, this.minDist, this.maxDist);
      this.target.lerp(this.desired, k);
      this.distance = M.lerp(this.distance, this.desiredDistance, k);
      this.yaw = M.lerpAngle(this.yaw, this.desiredYaw, k);
      this._applyPose();
    }

    // 视口 CSS 像素 → 地面（y = 0.5）交点，世界坐标
    groundPoint(x, y) {
      const r = this.dom ? this.dom.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
      if (!r.width || !r.height) return null;
      _ndc.set(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
      _ray.setFromCamera(_ndc, this.camera);
      if (!_ray.ray.intersectPlane(_plane, _hit)) return null;
      return _hit.clone();
    }

    dispose() {
      for (const [el, ev, fn, opt] of this._handlers) el.removeEventListener(ev, fn, opt);
      this._handlers.length = 0;
    }

    // ------------------------------------------------------------ 内部 --
    _clampDesired() {
      const b = this.bounds;
      this.desired.x = M.clamp(this.desired.x, b.xMin, b.xMax);
      this.desired.z = -M.clamp(-this.desired.z, b.yMin, b.yMax);
    }
    _currentPitch() {
      return M.lerp(this.pitch - 10, this.pitch + 8, M.inverseLerp(this.minDist, this.maxDist, this.distance));
    }
    // Quaternion.Euler(pitch, yaw, 0)：位置 = 目标 - 前方 × 距离
    _applyPose() {
      const p = this._currentPitch() * M.deg2rad, y = this.yaw * M.deg2rad;
      const fx = Math.sin(y) * Math.cos(p), fy = -Math.sin(p), fz = -Math.cos(y) * Math.cos(p); // three 坐标
      const cam = this.camera;
      cam.position.set(this.target.x - fx * this.distance, this.target.y - fy * this.distance, this.target.z - fz * this.distance);
      cam.up.set(0, 1, 0);
      cam.lookAt(this.target);
      cam.updateMatrixWorld();
    }

    _on(el, ev, fn, opt) { el.addEventListener(ev, fn, opt); this._handlers.push([el, ev, fn, opt]); }
    _bind() {
      const el = this.dom;
      el.style.touchAction = 'none';
      this._on(el, 'pointerdown', e => this._down(e));
      this._on(el, 'pointermove', e => this._move(e));
      this._on(el, 'pointerup', e => this._up(e, false));
      this._on(el, 'pointercancel', e => this._up(e, true));
      this._on(el, 'lostpointercapture', e => { if (this._pointers.has(e.pointerId)) this._up(e, true); });
      this._on(el, 'wheel', e => this._wheel(e), { passive: false });
      this._on(el, 'contextmenu', e => e.preventDefault());
      this._on(el, 'gesturestart', e => e.preventDefault()); // iOS Safari 原生缩放手势
      this._on(window, 'keydown', e => this._key(e, true));
      this._on(window, 'keyup', e => this._key(e, false));
      this._on(window, 'blur', () => this._keys.clear());
    }

    _down(e) {
      if (!this.inputEnabled) return;
      if (e.pointerType === 'mouse' && e.button !== 0 && e.button !== 2) return;
      try { this.dom.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ }
      this._pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, button: e.button, type: e.pointerType });
      if (this._pointers.size >= 2) {
        // 双指：缩放（并以中点平移），取消点击
        this._pressing = false; this._dragging = true;
        this._lastPinch = this._pinchDist();
        const mid = this._mid();
        this._lastMid = this.groundPoint(mid.x, mid.y);
        return;
      }
      this._pressing = true; this._dragging = false;
      this._pressId = e.pointerId; this._pressX = e.clientX; this._pressY = e.clientY;
      this._lastGround = this.groundPoint(e.clientX, e.clientY);
      if (e.pointerType === 'mouse') e.preventDefault();
    }

    _move(e) {
      const p = this._pointers.get(e.pointerId);
      if (!p) return;
      p.x = e.clientX; p.y = e.clientY;
      if (!this.inputEnabled) return;
      if (this._pointers.size >= 2) {
        const d = this._pinchDist();
        if (this._lastPinch > 0) this.desiredDistance *= this._lastPinch / Math.max(1, d);
        this._lastPinch = d;
        const mid = this._mid();
        const g = this.groundPoint(mid.x, mid.y);
        if (g && this._lastMid) {
          this._pan(this._lastMid.x - g.x, this._lastMid.z - g.z);
          this._lastMid = this.groundPoint(mid.x, mid.y);
        } else this._lastMid = g;
        return;
      }
      if (this._pressing && e.pointerId === this._pressId) this._drag(e.clientX, e.clientY);
    }

    _up(e, cancelled) {
      const p = this._pointers.get(e.pointerId);
      if (!p) return;
      this._pointers.delete(e.pointerId);
      try { if (this.dom.hasPointerCapture && this.dom.hasPointerCapture(e.pointerId)) this.dom.releasePointerCapture(e.pointerId); } catch (err) { /* 忽略 */ }
      if (this._pointers.size >= 2) {
        // 三指变双指：以新的两指重新取缩放基准与中点，避免缩放 / 平移跳变
        this._lastPinch = this._pinchDist();
        const m = this._mid();
        this._lastMid = this.groundPoint(m.x, m.y);
        return;
      }
      if (this._pointers.size === 1) {
        // 双指变单指：剩下的手指继续拖动（不触发点击）
        const [id, q] = this._pointers.entries().next().value;
        this._pressing = true; this._dragging = true;
        this._pressId = id; this._pressX = q.x; this._pressY = q.y;
        this._lastGround = this.groundPoint(q.x, q.y);
        this._lastPinch = -1; this._lastMid = null;
        return;
      }
      if (this._pointers.size === 0) { this._lastPinch = -1; this._lastMid = null; }
      if (!this._pressing || e.pointerId !== this._pressId) return;
      const isTap = !cancelled && !this._dragging && this.inputEnabled && !(p.type === 'mouse' && p.button !== 0);
      this._pressing = false;
      this._pressId = -1;
      if (isTap) {
        for (const cb of this.onTap.slice()) {
          try { cb(e.clientX, e.clientY); } catch (err) { console.error(err); }
        }
      }
    }

    _drag(x, y) {
      if (!this._dragging && Math.hypot(x - this._pressX, y - this._pressY) > DRAG_THRESHOLD) this._dragging = true;
      if (!this._dragging) return;
      const g = this.groundPoint(x, y);
      if (!g) return;
      if (!this._lastGround) { this._lastGround = g; return; }
      this._pan(this._lastGround.x - g.x, this._lastGround.z - g.z);
      // 相机移动后，同一屏幕点对应的地面
      this._lastGround = this.groundPoint(x, y);
    }
    _pan(dx, dz) {
      this.desired.x += dx; this.desired.z += dz;
      this.target.x += dx; this.target.z += dz;
      this._applyPose();
    }
    _pinchDist() {
      const it = this._pointers.values();
      const a = it.next().value, b = it.next().value;
      return Math.hypot(a.x - b.x, a.y - b.y);
    }
    _mid() {
      const it = this._pointers.values();
      const a = it.next().value, b = it.next().value;
      return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    }

    _wheel(e) {
      e.preventDefault();
      if (!this.inputEnabled) return;
      let steps;
      if (e.deltaMode === 1) steps = -e.deltaY / 3;          // 行
      else if (e.deltaMode === 2) steps = -e.deltaY * 3;     // 页
      else steps = -e.deltaY * (e.ctrlKey ? 0.03 : 0.01);    // 像素（ctrlKey = 触控板捏合）
      steps = M.clamp(steps, -4, 4);
      this.desiredDistance *= Math.pow(0.88, steps);
    }

    _key(e, down) {
      const k = e.code || e.key;
      if (!down) { this._keys.delete(k); return; }
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return;
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      this._keys.add(k);
    }
    _keyboard(dt) {
      if (this._keys.size === 0) return;
      if (SG.UI && typeof SG.UI.anyModal === 'function' && SG.UI.anyModal()) return;
      const K = this._keys;
      let h = 0, v = 0;
      if (K.has('KeyD') || K.has('ArrowRight') || K.has('d') || K.has('D')) h += 1;
      if (K.has('KeyA') || K.has('ArrowLeft') || K.has('a') || K.has('A')) h -= 1;
      if (K.has('KeyW') || K.has('ArrowUp') || K.has('w') || K.has('W')) v += 1;
      if (K.has('KeyS') || K.has('ArrowDown') || K.has('s') || K.has('S')) v -= 1;
      if (h !== 0 || v !== 0) {
        // Quaternion.Euler(0, Yaw, 0) * (h, 0, v)，Unity → three（z 取反）
        const y = this.yaw * M.deg2rad, s = dt * this.distance * 0.9;
        const ux = h * Math.cos(y) + v * Math.sin(y), uz = -h * Math.sin(y) + v * Math.cos(y);
        this.desired.x += ux * s; this.desired.z -= uz * s;
      }
      if (K.has('KeyQ') || K.has('q') || K.has('Q')) this.desiredYaw -= 60 * dt;
      if (K.has('KeyE') || K.has('e') || K.has('E')) this.desiredYaw += 60 * dt;
    }
  }

  SG.CameraRig = CameraRig;
})();
