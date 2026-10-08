'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 俯视相机
   ← View/CameraRig.cs：拖动平移、滚轮 / 双指缩放、点击选取、WASD / 方向键平移、Q/E 旋转
   输入只监听渲染画布（Pointer Events），DOM 界面自然会挡住画布上的操作。

   第二版（§4G 世界地图）新增：
     setBounds({ xMin, yMin, xMax, yMax })   限制镜头目标点的范围（地图坐标）
     setRegion(key | rect, pad = 0)          按 SG.World.regions[key]（或经纬度 / 地图矩形）设定范围
     useMap(mapView)                         按地图的构建范围配置镜头：'world' → 世界模式（见 setWorldMode），
                                             范围 = 整个世界；'china' → 与旧版完全相同
     setWorldMode(on)                        世界模式：可拉远到 maxDist 340 看到整个地区（俯角在 130 以内与旧版相同），
                                             远距离 focusMap / focusWorld 自动改为平滑飞行
     flyTo(mapX, mapY, dist, { duration, arc }) → Promise   平滑飞行（远距离时先拉高再降落），用户操作即中止
     fitRect({ xMin, yMin, xMax, yMax }, instant)          选择能看到整个矩形的距离并对准
     flying                                   是否正在飞行
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
  const WORLD_MAX_DIST = 340;   // 世界模式的最远距离：可看到一整个地区（如整个欧洲或印度）

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
      // 世界模式（见 setWorldMode）：俯角按 pitchRefDist 以内的距离计算，autoFly 时远距离对焦改为飞行
      this.pitchRefDist = 0;
      this.autoFly = false;
      this.autoFlyMin = 60;
      this._fly = null;

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
      // 世界模式：远距离对焦改为平滑飞行
      if (!instant && this.autoFly) {
        const dx = p.x - this.target.x, dz = p.z - this.target.z;
        if (Math.hypot(dx, dz) > this.autoFlyMin) { this.flyTo(p.x, -p.z, dist); return; }
      }
      this._endFly();
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

    // 镜头目标点的范围（地图坐标 Unity x / z）
    setBounds(b) {
      if (!b) return;
      this.bounds = { xMin: Math.min(b.xMin, b.xMax), yMin: Math.min(b.yMin, b.yMax), xMax: Math.max(b.xMin, b.xMax), yMax: Math.max(b.yMin, b.yMax) };
      this._clampDesired();
    }
    // 地区（SG.World.regions 的键，或 { lon0, lon1, lat0, lat1 } / { xMin, yMin, xMax, yMax }）→ 镜头范围；pad 为四周外扩
    setRegion(r, pad) {
      const W = SG.World;
      const rect = W && W.regionRect ? W.regionRect(r) : (r && r.xMin !== undefined ? r : null);
      if (!rect) return null;
      pad = pad || 0;
      this.setBounds({ xMin: rect.xMin - pad, yMin: rect.yMin - pad, xMax: rect.xMax + pad, yMax: rect.yMax + pad });
      return this.bounds;
    }
    // 世界模式：可拉远看到整个地区；俯角在 130 以内与旧版相同，更远时再略抬高；远距离对焦自动飞行
    setWorldMode(on) {
      if (on) { this.maxDist = WORLD_MAX_DIST; this.pitchRefDist = 130; this.autoFly = true; }
      else { this.maxDist = 130; this.pitchRefDist = 0; this.autoFly = false; }
      // 远裁剪面：世界模式拉远时远处的地区不被裁掉（旧版 700）
      const cam = this.camera;
      if (cam && cam.isPerspectiveCamera) {
        const far = on ? 1500 : 700;
        if (cam.far !== far) { cam.far = far; cam.updateProjectionMatrix(); }
      }
      this.desiredDistance = M.clamp(this.desiredDistance, this.minDist, this.maxDist);
    }
    // 按 MapView 的构建范围配置：世界地图 → 世界模式 + 全图范围；中国地图 → 旧版设定
    useMap(map) {
      const world = !!(map && map.region === 'world');
      this.setWorldMode(world);
      if (map && map.bounds) this.setBounds(map.bounds);
    }
    // 平滑飞行到地图坐标（远距离时先拉高再降落）。返回 Promise（到达或被中止时完成）
    flyTo(mapX, mapY, dist, opts) {
      opts = opts || {};
      this._endFly();
      const b = this.bounds;
      const to = new THREE.Vector3(M.clamp(mapX, b.xMin, b.xMax), 0, -M.clamp(mapY, b.yMin, b.yMax));
      const from = this.target.clone();
      const d0 = this.distance, d1 = M.clamp(dist > 0 ? dist : this.desiredDistance, this.minDist, this.maxDist);
      const travel = Math.hypot(to.x - from.x, to.z - from.z);
      const dur = opts.duration > 0 ? opts.duration : M.clamp(0.45 + travel / 240, 0.5, 2.4);
      const arc = opts.arc === false ? 0 : Math.max(0, Math.min(this.maxDist, Math.max(d0, d1) + travel * 0.3) - Math.max(d0, d1));
      return new Promise(resolve => {
        this._fly = { from, to, d0, d1, arc, t: 0, dur, resolve };
      });
    }
    get flying() { return !!this._fly; }
    // 能看到整个矩形（地图坐标）所需的镜头距离（按当前视口宽高比与俯角估算，未按 minDist / maxDist 限制）
    fitDistance(r) {
      const cam = this.camera;
      const aspect = cam && cam.aspect > 0 ? cam.aspect : 16 / 9;
      const fov = (cam && cam.fov ? cam.fov : 34) * M.deg2rad;
      const w = Math.abs(r.xMax - r.xMin), h = Math.abs(r.yMax - r.yMin);
      const pitch = (this.pitch + 4) * M.deg2rad;
      const dH = h * Math.sin(pitch) / (2 * Math.tan(fov / 2)) * 1.05;
      const dW = w / (2 * Math.tan(fov / 2) * aspect) * 1.05;
      return Math.max(dH, dW);
    }
    // 对准矩形中心，并选择能看到整个矩形的距离（按当前视口宽高比与俯角估算）
    fitRect(r, instant) {
      const d = M.clamp(this.fitDistance(r), this.minDist, this.maxDist);
      const cx = (r.xMin + r.xMax) / 2, cy = (r.yMin + r.yMax) / 2;
      if (instant) this.focusMap(cx, cy, d, true);
      else return this.flyTo(cx, cy, d);
      return Promise.resolve();
    }
    _endFly() {
      const f = this._fly;
      if (!f) return;
      this._fly = null;
      this.desired.copy(this.target);
      this.desiredDistance = this.distance;
      try { f.resolve(); } catch (e) { /* 忽略 */ }
    }

    update(dt) {
      if (!(dt >= 0)) dt = 0;
      if (this.inputEnabled) this._keyboard(dt);
      const f = this._fly;
      if (f) {
        // 飞行：目标点按缓动曲线移动，距离 = 起止距离插值 + 中途抬高
        f.t = Math.min(1, f.t + dt / f.dur);
        const t = f.t, k = t * t * t * (t * (t * 6 - 15) + 10);
        this.target.lerpVectors(f.from, f.to, k);
        this.distance = M.lerp(f.d0, f.d1, k) + f.arc * Math.sin(Math.PI * k);
        this.desired.copy(this.target);
        this.desiredDistance = M.clamp(this.distance, this.minDist, this.maxDist);
        this.yaw = M.lerpAngle(this.yaw, this.desiredYaw, 1 - Math.exp(-dt * 10));
        if (t >= 1) {
          this._fly = null;
          this.distance = this.desiredDistance = f.d1;
          try { f.resolve(); } catch (e) { /* 忽略 */ }
        }
        this._applyPose();
        return;
      }
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
      // 世界模式：pitchRefDist（130）以内与旧版（maxDist 130）完全相同，更远时每 10 单位再抬高约 0.8°（最多 6°）
      const ref = this.pitchRefDist > 0 ? Math.min(this.maxDist, this.pitchRefDist) : this.maxDist;
      let p = M.lerp(this.pitch - 10, this.pitch + 8, M.inverseLerp(this.minDist, ref, this.distance));
      if (this.pitchRefDist > 0 && this.distance > ref) p += Math.min(6, (this.distance - ref) * 0.08);
      return p;
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
      this._endFly();
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
      this._endFly();
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
        this._endFly();
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
