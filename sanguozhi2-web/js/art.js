'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 渲染基础
   ← View/Art.cs（材质、程序化纹理、低多边形网格工具、常用模型）
   ← Resources/Shaders/*.shader（LowPoly 半兰伯特、Water、Sky、Unlit、Additive）
   ← Core/Game.cs SetupWorld()（太阳、三色环境光、雾、天空盒、阴影）

   第三版（DESIGN-V3 §1：平滑、细致）新增 / 改进：
     MeshBuilder.triN(a, b, c, na, nb, nc, ca, cb?, cc?)   逐顶点法线与颜色的三角形（正反面按法线自动确定）
     MeshBuilder.sphere(center, radius(数或 Vector3), col, detail = 1, { seed, rough, top, flat, bottomShade })   平滑（可起伏的）椭球
     MeshBuilder.lathe(base, [[r, y, col?, sharp?], …], seg, col, { capTop, capBottom })   平滑旋转体（穹顶、塔、帐、树冠层）
     MeshBuilder.smoothCylinder(bottom, r0, r1, h, seg, col, cap)   MeshBuilder.tube(points, radii, seg, col, capEnd)
     MeshBuilder.chineseRoof(...)   参数不变；改为凹曲屋面、翼角起翘、平滑法线并加鸱吻（所有用到它的场景一起变得细致）
     Models.tree(...)               参数不变；平滑树干与圆润树冠
     Gfx.terrainDetail() / terrainDetailGen()   程序化地形细节纹理（草、土、沙纹、岩石、农田地块、犁沟）
     Gfx.masonryMaterial(near?, far?)   城池砖石材质（墙面错缝砖、坡面筒瓦、平顶方砖，按世界坐标程序化，随距离淡出）
     Gfx.terrainMaterial(opts)      地形着色器（LowPoly 光照之上按坡度 / 高度 / 群落混合草地、泥土、农田、沙、
                                    三平面投影岩石、雪、海床，再乘凹凸遮蔽）。opts.maps → 战略地图的数据纹理版；
                                    缺省为顶点版：顶点色 = 群落底色，attribute vec4 aMat = (沙, 岩石倾向, 雪, 1 − 农田)
   原有接口（Gfx.lowPoly / newLowPoly / water / unlit / additive、MeshBuilder 的平面着色方法、Models.soldier / commander / city）不变。

   颜色约定：关闭 three.js 的颜色管理，按 Unity「Gamma 空间」工作——
   C# 里的颜色数字（new Color(r,g,b)、'#rrggbb'）原样作为顶点色 / 材质色使用，
   输出不做 sRGB 编码。因此 new THREE.Color(r,g,b)、SG.Gfx.color(...)、
   十六进制字符串三者含义一致，各模块无需关心色彩空间。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  if (THREE.ColorManagement) THREE.ColorManagement.enabled = false;

  // ------------------------------------------------------------- 颜色工具 --
  // color(r, g, b) | color('#hex' | 'rgb()/rgba()' | 名称) | color([r,g,b(,a)]) | color({r,g,b}) | color(THREE.Color)
  function color(a, g, b) {
    if (typeof a === 'number' && typeof g === 'number') return new THREE.Color(a, g, b);
    if (typeof a === 'number') return new THREE.Color(a); // 0xrrggbb
    if (a && a.isColor) return a.clone();
    if (Array.isArray(a)) return new THREE.Color(a[0], a[1], a[2]);
    if (typeof a === 'string') {
      const m = /^\s*rgba?\(([^)]+)\)\s*$/i.exec(a);
      if (m) {
        const p = m[1].split(/[\s,\/]+/).filter(s => s.length).map(parseFloat);
        return new THREE.Color(p[0] / 255, p[1] / 255, p[2] / 255);
      }
      return new THREE.Color(a);
    }
    if (a && typeof a.r === 'number') return new THREE.Color(a.r, a.g, a.b);
    return new THREE.Color(1, 1, 1);
  }
  // 解析颜色并取出透明度（Unity Color 的 a 分量）
  function colorAlpha(a, g, b, alpha) {
    if (typeof a === 'number' && typeof g === 'number') return { color: new THREE.Color(a, g, b), a: alpha === undefined ? 1 : alpha };
    if (Array.isArray(a)) return { color: new THREE.Color(a[0], a[1], a[2]), a: a.length > 3 ? a[3] : 1 };
    if (typeof a === 'string') {
      const m = /^\s*rgba?\(([^)]+)\)\s*$/i.exec(a);
      if (m) {
        const p = m[1].split(/[\s,\/]+/).filter(s => s.length).map(parseFloat);
        return { color: new THREE.Color(p[0] / 255, p[1] / 255, p[2] / 255), a: p.length > 3 ? p[3] : 1 };
      }
      if (/^#[0-9a-f]{8}$/i.test(a)) return { color: new THREE.Color(a.slice(0, 7)), a: parseInt(a.slice(7, 9), 16) / 255 };
      return { color: new THREE.Color(a), a: 1 };
    }
    if (a && typeof a.r === 'number') return { color: new THREE.Color(a.r, a.g, a.b), a: typeof a.a === 'number' ? a.a : 1 };
    return { color: new THREE.Color(1, 1, 1), a: 1 };
  }
  // Art.Shade：k>0 向白色插值，k<0 向黑色插值
  function shade(c, k) {
    const s = (c && c.isColor) ? c : color(c);
    if (k >= 0) return new THREE.Color(s.r + (1 - s.r) * Math.min(1, k), s.g + (1 - s.g) * Math.min(1, k), s.b + (1 - s.b) * Math.min(1, k));
    const t = 1 - Math.min(1, -k);
    return new THREE.Color(s.r * t, s.g * t, s.b * t);
  }
  // Color.Lerp
  function lerpColor(a, b, t) {
    t = M.clamp01(t);
    return new THREE.Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
  }

  // ------------------------------------------------------------- 程序化纹理 --
  function alphaTexture(n, alphaAt) {
    const data = new Uint8Array(n * n * 4);
    for (let y = 0; y < n; y++)
      for (let x = 0; x < n; x++) {
        const i = (y * n + x) * 4;
        data[i] = data[i + 1] = data[i + 2] = 255;
        data[i + 3] = Math.round(M.clamp01(alphaAt(x, y, n)) * 255);
      }
    const t = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
    t.magFilter = THREE.LinearFilter;
    t.minFilter = THREE.LinearMipmapLinearFilter;
    t.generateMipmaps = true;
    t.needsUpdate = true;
    return t;
  }
  let softDot = null, ring = null;
  function softDotTexture() {
    if (softDot) return softDot;
    softDot = alphaTexture(64, (x, y, n) => {
      const d = Math.hypot(x + 0.5 - n / 2, y + 0.5 - n / 2) / (n / 2);
      const a = M.clamp01(1 - d);
      return a * a;
    });
    return softDot;
  }
  // 圆环纹理（选择光圈）
  function ringTexture() {
    if (ring) return ring;
    ring = alphaTexture(128, (x, y, n) => {
      const d = Math.hypot(x + 0.5 - n / 2, y + 0.5 - n / 2) / (n / 2);
      let a = M.clamp01(1 - Math.abs(d - 0.82) / 0.12);
      a = a * a + M.clamp01(0.8 - d) * 0.18;
      return a;
    });
    return ring;
  }

  // 可平铺的涟漪法线贴图（RG = 斜率），供水面高光使用；mipmap 让远处自然变平
  let ripple = null;
  function rippleTexture() {
    if (ripple) return ripple;
    const N = 128, rnd = SG.SeededRandom(1234);
    const hgt = new Float32Array(N * N);
    for (const [period, amp] of [[8, 1], [16, 0.5], [32, 0.25]]) {
      const lat = new Float32Array(period * period);
      for (let i = 0; i < lat.length; i++) lat[i] = rnd.nextDouble();
      const cell = N / period;
      for (let y = 0; y < N; y++)
        for (let x = 0; x < N; x++) {
          const gx = x / cell, gy = y / cell, ix = Math.floor(gx), iy = Math.floor(gy);
          let fx = gx - ix, fy = gy - iy;
          fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
          const x0 = ix % period, x1 = (ix + 1) % period, y0 = iy % period, y1 = (iy + 1) % period;
          const a = lat[y0 * period + x0], b = lat[y0 * period + x1], c = lat[y1 * period + x0], d = lat[y1 * period + x1];
          hgt[y * N + x] += amp * ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy);
        }
    }
    const sl = new Float32Array(N * N * 2);
    let mx = 1e-6;
    for (let y = 0; y < N; y++)
      for (let x = 0; x < N; x++) {
        const sx = hgt[y * N + (x + 1) % N] - hgt[y * N + (x + N - 1) % N];
        const sy = hgt[((y + 1) % N) * N + x] - hgt[((y + N - 1) % N) * N + x];
        sl[(y * N + x) * 2] = sx; sl[(y * N + x) * 2 + 1] = sy;
        mx = Math.max(mx, Math.abs(sx), Math.abs(sy));
      }
    const data = new Uint8Array(N * N * 4);
    for (let i = 0; i < N * N; i++) {
      data[i * 4] = Math.round((sl[i * 2] / mx * 0.5 + 0.5) * 255);
      data[i * 4 + 1] = Math.round((sl[i * 2 + 1] / mx * 0.5 + 0.5) * 255);
      data[i * 4 + 2] = 128; data[i * 4 + 3] = 255;
    }
    ripple = new THREE.DataTexture(data, N, N, THREE.RGBAFormat);
    ripple.wrapS = ripple.wrapT = THREE.RepeatWrapping;
    ripple.magFilter = THREE.LinearFilter;
    ripple.minFilter = THREE.LinearMipmapLinearFilter;
    ripple.generateMipmaps = true;
    ripple.needsUpdate = true;
    return ripple;
  }

  // ------------------------------------------------------------- 共享 uniform --
  const shared = {
    time: { value: 0 },
    equator: { value: new THREE.Color() },      // 三色环境光的「地平线」色（已乘强度）
    shadowStrength: { value: 0.55 },            // Unity sun.shadowStrength
  };

  // ------------------------------------------------------------- LowPoly 材质 --
  // Lambert + 半兰伯特（ndl = (n·l·0.5+0.5)²·1.15）+ 三色环境光 + 阴影强度 + 自发光 / 闪白
  const CH = THREE.ShaderChunk;
  const LP_LIGHTS_PARS = CH.lights_pars_begin.replace(
    'vec3 irradiance = mix( hemiLight.groundColor, hemiLight.skyColor, hemiDiffuseWeight );',
    'vec3 irradiance = dotNL >= 0.0 ? mix( sgEquator, hemiLight.skyColor, dotNL ) : mix( sgEquator, hemiLight.groundColor, - dotNL );');
  const LP_LAMBERT_PARS = CH.lights_lambert_pars_fragment.replace(
    'float dotNL = saturate( dot( geometryNormal, directLight.direction ) );',
    'float sgHL = dot( geometryNormal, directLight.direction ) * 0.5 + 0.5;\n\tfloat dotNL = sgHL * sgHL * 1.15;');
  const LP_SHADOW_PARS = CH.shadowmap_pars_fragment.split('return shadow;').join('return mix( 1.0, shadow, sgShadowStrength );');
  const LP_HEADER = 'uniform float sgEmission;\nuniform float sgFlash;\nuniform vec3 sgFlashColor;\nuniform vec3 sgEquator;\nuniform float sgShadowStrength;\n';

  function lowPolyCompile(shader) {
    const u = this.sg;
    shader.uniforms.sgEmission = u.emission;
    shader.uniforms.sgFlash = u.flash;
    shader.uniforms.sgFlashColor = u.flashColor;
    shader.uniforms.sgEquator = shared.equator;
    shader.uniforms.sgShadowStrength = shared.shadowStrength;
    shader.fragmentShader = LP_HEADER + shader.fragmentShader
      .replace('#include <lights_pars_begin>', LP_LIGHTS_PARS)
      .replace('#include <lights_lambert_pars_fragment>', LP_LAMBERT_PARS)
      .replace('#include <shadowmap_pars_fragment>', LP_SHADOW_PARS)
      .replace('#include <color_fragment>', '#include <color_fragment>\n\tvec3 sgBase = diffuseColor.rgb;\n\tdiffuseColor.rgb = mix( diffuseColor.rgb, sgFlashColor, sgFlash );')
      .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\n\ttotalEmissiveRadiance += sgBase * sgEmission + sgFlashColor * ( sgFlash * 0.6 );');
  }

  class LowPolyMaterial extends THREE.MeshLambertMaterial {
    constructor(params) {
      super(Object.assign({ vertexColors: true }, params || {}));
      this.sg = { emission: { value: 0 }, flash: { value: 0 }, flashColor: { value: new THREE.Color(1, 1, 1) } };
      this.onBeforeCompile = lowPolyCompile;
    }
    // _Emission：顶点色 × emission 叠加为自发光
    get emission() { return this.sg.emission.value; }
    set emission(v) { this.sg.emission.value = v; }
    // _Flash / _FlashColor：受击闪白
    get flash() { return this.sg.flash.value; }
    set flash(v) { this.sg.flash.value = v; }
    get flashColor() { return this.sg.flashColor.value; }
    set flashColor(c) { this.sg.flashColor.value = color(c); }
    copy(src) {
      super.copy(src);
      this.sg = {
        emission: { value: src.sg ? src.sg.emission.value : 0 },
        flash: { value: src.sg ? src.sg.flash.value : 0 },
        flashColor: { value: src.sg ? src.sg.flashColor.value.clone() : new THREE.Color(1, 1, 1) },
      };
      this.onBeforeCompile = lowPolyCompile;
      return this;
    }
  }

  // ------------------------------------------------------------- 水面材质 --
  const WATER_VS = `
uniform float uTime;
uniform float uAmp;
varying vec3 vWorld;
varying float vShore;
#include <fog_pars_vertex>
void main() {
  vec4 w = modelMatrix * vec4( position, 1.0 );
  float ux = w.x;
  float uz = - w.z;   // Unity 坐标
  float h = sin( uTime * 1.3 + ux * 0.55 + uz * 0.35 ) * 0.6 + sin( uTime * 0.9 - ux * 0.27 + uz * 0.71 ) * 0.4;
#ifdef USE_COLOR
  vShore = color.r;
  float waveK = 1.0 - color.g;   // 顶点色 g = 1：远海不做顶点起伏（避免与细网格拼接处出现裂缝）
#else
  vShore = 0.0;
  float waveK = 1.0;
#endif
  w.y += h * uAmp * waveK;
  vWorld = w.xyz;
  vec4 mvPosition = viewMatrix * w;
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const WATER_FS = `
uniform float uTime;
uniform float uAmp;
uniform float uAlpha;
uniform vec3 uDeep;
uniform vec3 uShallow;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform vec3 uAmbient;
uniform vec3 uSheenDir;
uniform vec3 uSheenColor;
uniform vec3 uSkyColor;
uniform sampler2D uRipple;
varying vec3 vWorld;
varying float vShore;
#include <common>
#include <fog_pars_fragment>
void main() {
  float ux = vWorld.x;
  float uz = - vWorld.z;
  float t = uTime;
  float a1 = t * 1.3 + ux * 0.55 + uz * 0.35;
  float a2 = t * 0.9 - ux * 0.27 + uz * 0.71;
  float dx = cos( a1 ) * 0.33 - cos( a2 ) * 0.11;
  float dz = cos( a1 ) * 0.21 + cos( a2 ) * 0.28;
  // Unity 法线 (-dx·A·4, 1, -dz·A·4)，转换到 three（z 取反）
  vec3 n = normalize( vec3( - dx * uAmp * 4.0, 1.0, dz * uAmp * 4.0 ) );
  float shore = saturate( vShore );
  // 闪光点；远处一个周期不足几个像素时淡出，避免摩尔纹
  float fw = max( fwidth( ux * 3.1 ), fwidth( uz * 2.7 ) );
  float sparkle = sin( ux * 3.1 + t * 2.0 ) * sin( uz * 2.7 - t * 1.7 );
  vec3 albedo = mix( uDeep, uShallow, shore ) + saturate( sparkle - 0.85 ) * 0.6 * ( 1.0 - smoothstep( 0.3, 0.9, fw ) );
  vec3 V = normalize( cameraPosition - vWorld );
  // 高光用的法线：正弦波 + 两层滚动的涟漪贴图（不规则，远处 mip 自动变平）
  vec2 r1 = texture2D( uRipple, vec2( ux, uz ) * 0.045 + vec2( t * 0.012, t * 0.008 ) ).rg * 2.0 - 1.0;
  vec2 r2 = texture2D( uRipple, vec2( ux, uz ) * 0.093 + vec2( - t * 0.010, t * 0.015 ) ).rg * 2.0 - 1.0;
  vec2 rip = ( r1 + r2 * 0.7 ) * 0.17;
  vec3 ns = normalize( vec3( - dx * uAmp * 2.0 + rip.x, 1.0, dz * uAmp * 2.0 - rip.y ) );
  // BlinnPhong（Specular 0.35 → 指数 44.8，Gloss 0.8，_SpecColor 0.5）
  vec3 L = normalize( uSunDir );
  float ndl = max( dot( n, L ), 0.0 );
  float spec = pow( max( dot( ns, normalize( L + V ) ), 0.0 ), 44.8 ) * 0.8;
  vec3 col = albedo * ( uSunColor * ndl + uAmbient ) + uSunColor * 0.5 * spec;
  // 风格化：掠射角天空反射 + 朝天空盒太阳方向的柔和光带与碎光
  vec3 up = vec3( 0.0, 1.0, 0.0 );
  float fres = pow( 1.0 - max( dot( up, V ), 0.0 ), 5.0 );
  vec3 S = normalize( uSheenDir );
  float glow = pow( max( dot( reflect( - V, up ), S ), 0.0 ), 10.0 );
  float glint = pow( max( dot( ns, normalize( S + V ) ), 0.0 ), 60.0 );
  col = mix( col, uSkyColor, fres * 0.45 ) + uSheenColor * ( glow * 0.16 + glint * 0.26 );
  // 岸边浪花
  float foam = smoothstep( 0.86, 0.99, shore ) * ( 0.55 + 0.45 * sin( t * 1.6 + ( ux + uz ) * 0.9 + shore * 18.0 ) );
  col = mix( col, vec3( 0.93, 0.95, 0.92 ), foam * 0.22 );
  float alpha = mix( uAlpha, uAlpha * 0.75, shore );
  gl_FragColor = vec4( col, alpha );
  #include <fog_fragment>
}`;

  // ------------------------------------------------------------- 天空 --
  const SKY_VS = `
varying vec3 vDir;
void main() {
  vDir = position;
  vec4 p = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );
  gl_Position = p.xyww;
}`;
  const SKY_FS = `
uniform vec3 uTop;
uniform vec3 uHorizon;
uniform vec3 uBottom;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
varying vec3 vDir;
void main() {
  vec3 d = normalize( vDir );
  float y = d.y;
  vec3 c = y > 0.0 ? mix( uHorizon, uTop, pow( clamp( y, 0.0, 1.0 ), 0.55 ) ) : mix( uHorizon, uBottom, clamp( - y * 3.0, 0.0, 1.0 ) );
  float s = clamp( dot( d, normalize( uSunDir ) ), 0.0, 1.0 );
  c += uSunColor * ( pow( s, 64.0 ) * 0.9 + pow( s, 6.0 ) * 0.18 );
  gl_FragColor = vec4( c, 1.0 );
}`;

  // ------------------------------------------------------------- Gfx --
  // 光照参数（Game.SetupWorld）。three 的物理光强需乘 π；EXPOSURE 把 Unity 的数值映射到合适的亮度。
  const SUN_COLOR = [1, 0.94, 0.84];
  const SUN_INTENSITY = 1.15;
  const AMB_SKY = [0.62, 0.68, 0.8], AMB_EQUATOR = [0.58, 0.56, 0.5], AMB_GROUND = [0.32, 0.3, 0.26];
  const EXPOSURE_SUN = 0.75, EXPOSURE_AMB = 0.56;
  const FOG_COLOR = [0.86, 0.84, 0.78];

  // Unity Quaternion.Euler(48, -35, 0) * forward = 光线方向；指向太阳的方向取反后转换到 three（z 取反）
  function unityEulerForward(pitchDeg, yawDeg) {
    const p = pitchDeg * M.deg2rad, y = yawDeg * M.deg2rad;
    return { x: Math.sin(y) * Math.cos(p), y: -Math.sin(p), z: Math.cos(y) * Math.cos(p) };
  }
  const sunFwd = unityEulerForward(48, -35);
  const SUN_DIR = new THREE.Vector3(-sunFwd.x, -sunFwd.y, sunFwd.z).normalize(); // three：指向太阳

  const _v = new THREE.Vector3(), _v2 = new THREE.Vector3(), _ndc = new THREE.Vector2();
  const _ray = new THREE.Raycaster();
  const _basis = new THREE.Matrix4(), _bx = new THREE.Vector3(), _by = new THREE.Vector3(), _bz = new THREE.Vector3();
  const _origin = new THREE.Vector3(0, 0, 0), _up = new THREE.Vector3(0, 1, 0);

  const Gfx = {
    renderer: null, scene: null, camera: null, sun: null, hemi: null, sky: null,
    container: null, time: 0, sunDir: SUN_DIR.clone(),
    width: 1, height: 1, rect: { left: 0, top: 0, width: 1, height: 1 },
    // 自适应画质：持续掉帧（平均 > 28ms）时依次降低像素比、阴影贴图尺寸。设为 false 关闭
    autoQuality: true, maxPixelRatio: 2,
    _lowPoly: null, _water: null, _t0: 0, _perf: { last: 0, acc: 0, n: 0 },
    _screens: [],

    // ---- 全屏场景栈（攻击画面、单挑等）----
    // screen = { scene, camera, update?(dt), render?(renderer), resize?(w, h) }
    // 栈顶有场景时只渲染栈顶场景（主场景暂停绘制），并隐藏世界标签层（城名、部队名牌）。
    pushScreen(screen) {
      this._screens.push(screen);
      this._fitScreen(screen);
      this._syncScreenClass();
      return screen;
    },
    popScreen(screen) {
      const i = this._screens.indexOf(screen);
      if (i >= 0) this._screens.splice(i, 1);
      this._syncScreenClass();
    },
    topScreen() { return this._screens.length ? this._screens[this._screens.length - 1] : null; },
    _fitScreen(s) {
      const cam = s && s.camera;
      if (cam && cam.isPerspectiveCamera) { cam.aspect = this.width / this.height; cam.updateProjectionMatrix(); }
      if (s && typeof s.resize === 'function') s.resize(this.width, this.height);
    },
    _syncScreenClass() {
      try { document.documentElement.classList.toggle('sg-screen-active', this._screens.length > 0); } catch (e) { /* 无 DOM */ }
    },

    init(container) {
      container = container || document.body;
      this.container = container;
      const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, this.maxPixelRatio));
      renderer.outputColorSpace = THREE.LinearSRGBColorSpace; // Gamma 工作流：颜色原样输出
      renderer.toneMapping = THREE.NoToneMapping;
      renderer.shadowMap.enabled = true;
      renderer.shadowMap.type = THREE.PCFSoftShadowMap;
      const cv = renderer.domElement;
      cv.style.display = 'block';
      cv.style.width = '100%';
      cv.style.height = '100%';
      cv.style.touchAction = 'none';
      cv.style.outline = 'none';
      cv.style.userSelect = 'none';
      cv.style.webkitUserSelect = 'none';
      cv.style.webkitTouchCallout = 'none';
      cv.style.webkitTapHighlightColor = 'rgba(0,0,0,0)';
      cv.setAttribute('tabindex', '-1');
      container.appendChild(cv);
      this.renderer = renderer;

      const scene = new THREE.Scene();
      scene.background = color(FOG_COLOR);
      scene.fog = new THREE.Fog(color(FOG_COLOR), 90, 320);
      this.scene = scene;

      this.camera = new THREE.PerspectiveCamera(34, 16 / 9, 0.5, 700);
      this.camera.position.set(60, 80, 40);
      this.camera.lookAt(60, 0, -50);
      scene.add(this.camera);

      // 太阳
      const sun = new THREE.DirectionalLight(color(SUN_COLOR), SUN_INTENSITY * Math.PI * EXPOSURE_SUN);
      sun.castShadow = true;
      sun.shadow.mapSize.set(2048, 2048);
      sun.shadow.bias = -0.0006;
      sun.shadow.normalBias = 0.035;
      const sc = sun.shadow.camera;
      sc.left = -60; sc.right = 60; sc.top = 60; sc.bottom = -60; sc.near = 1; sc.far = 420;
      scene.add(sun);
      scene.add(sun.target);
      this.sun = sun;

      // 三色环境光（天空 / 地平线 / 地面），地平线色由着色器补丁实现
      const hemi = new THREE.HemisphereLight(color(AMB_SKY), color(AMB_GROUND), Math.PI * EXPOSURE_AMB);
      hemi.position.set(0, 1, 0);
      scene.add(hemi);
      this.hemi = hemi;
      this.equatorColor = color(AMB_EQUATOR);

      // 天空穹顶（Sky.shader；_SunDir 使用材质默认值 (0.3,0.4,0.85)）
      const skyMat = new THREE.ShaderMaterial({
        uniforms: {
          uTop: { value: color(0.32, 0.55, 0.86) },
          uHorizon: { value: color(0.93, 0.88, 0.78) },
          uBottom: { value: color(0.55, 0.62, 0.66) },
          uSunDir: { value: new THREE.Vector3(0.3, 0.4, -0.85).normalize() },
          uSunColor: { value: color(1, 0.9, 0.7) },
        },
        vertexShader: SKY_VS, fragmentShader: SKY_FS,
        side: THREE.BackSide, depthWrite: false, depthTest: false, fog: false,
      });
      const sky = new THREE.Mesh(new THREE.SphereGeometry(400, 32, 16), skyMat);
      sky.frustumCulled = false;
      sky.renderOrder = -100000;
      sky.matrixAutoUpdate = true;
      scene.add(sky);
      this.sky = sky;
      this._skyBase = { top: skyMat.uniforms.uTop.value.clone(), horizon: skyMat.uniforms.uHorizon.value.clone(), bottom: skyMat.uniforms.uBottom.value.clone(), sun: skyMat.uniforms.uSunColor.value.clone() };

      // 低多边形工具包（js/lowpoly.js，SG.LP）接入：LP 着色器自行编码 sRGB（本渲染器线性输出 = Gamma 工作流）；
      // 主场景的氛围（SG.Gfx.mood）用同一套 DayNight 关键帧同时驱动 LP 材质与原有的 Lambert / 地形 / 水面材质（正午 = 原样）。
      this._initMood();

      this._t0 = performance.now();
      this.resize();
      const onResize = () => this.resize();
      window.addEventListener('resize', onResize);
      window.addEventListener('orientationchange', () => setTimeout(onResize, 120));
      if (window.visualViewport) window.visualViewport.addEventListener('resize', onResize);
      if (window.ResizeObserver) new ResizeObserver(onResize).observe(container);
      this.setShadowFocus(new THREE.Vector3(60, 0, -50), 80);
      return renderer;
    },

    resize() {
      if (!this.renderer) return;
      const c = this.container;
      const w = Math.max(1, (c && c.clientWidth) || window.innerWidth);
      const h = Math.max(1, (c && c.clientHeight) || window.innerHeight);
      const pr = Math.min(window.devicePixelRatio || 1, this.maxPixelRatio);
      if (this.renderer.getPixelRatio() !== pr) this.renderer.setPixelRatio(pr);
      if (w !== this.width || h !== this.height || this.renderer.domElement.width !== Math.round(w * pr)) {
        this.renderer.setSize(w, h, false);
        this.width = w; this.height = h;
        this.camera.aspect = w / h;
        this.camera.updateProjectionMatrix();
        for (const s of this._screens) this._fitScreen(s);
      }
      if (SG.LP) SG.LP.setViewport(Math.round(w * pr), Math.round(h * pr), 1080, 1.0);
      this._updateRect();
    },
    _updateRect() {
      const r = this.renderer.domElement.getBoundingClientRect();
      this.rect = { left: r.left, top: r.top, width: r.width || this.width, height: r.height || this.height };
    },

    // 让阴影相机始终覆盖可见区域；按阴影贴图像素对齐以避免平移时闪烁
    setShadowFocus(center, radius) {
      if (!this.sun) return;
      if (!(radius > 0)) radius = this.camera.position.distanceTo(center) * 0.95;
      radius = Math.ceil(M.clamp(radius, 16, 170) / 4) * 4;
      const sun = this.sun, size = sun.shadow.mapSize.x;
      const texel = (2 * radius) / size;
      const sunDir = this.sunDir || SUN_DIR;
      _basis.lookAt(sunDir, _origin, _up);
      _basis.extractBasis(_bx, _by, _bz);
      const px = center.dot(_bx), py = center.dot(_by);
      const sx = Math.round(px / texel) * texel - px, sy = Math.round(py / texel) * texel - py;
      _v.copy(center).addScaledVector(_bx, sx).addScaledVector(_by, sy);
      const D = 220;
      sun.target.position.copy(_v);
      sun.position.copy(_v).addScaledVector(sunDir, D);
      sun.target.updateMatrixWorld();
      sun.updateMatrixWorld();
      const sc = sun.shadow.camera;
      if (sc.right !== radius || sc.far !== D + radius * 2) {
        sc.left = -radius; sc.right = radius; sc.top = radius; sc.bottom = -radius;
        sc.near = 1; sc.far = D + radius * 2;
        sc.updateProjectionMatrix();
      }
    },

    // 世界坐标 → 视口 CSS 像素
    worldToScreen(p) {
      const cam = this.camera;
      _v.copy(p).applyMatrix4(cam.matrixWorldInverse);
      const inFront = _v.z < -cam.near;
      _v2.copy(p).project(cam);
      const r = this.rect;
      return {
        x: r.left + (_v2.x * 0.5 + 0.5) * r.width,
        y: r.top + (-_v2.y * 0.5 + 0.5) * r.height,
        visible: inFront && _v.z > -cam.far,
        dist: cam.position.distanceTo(p),
      };
    },
    // 视口 CSS 像素 → 世界射线
    screenRay(x, y) {
      this._updateRect();
      const r = this.rect;
      _ndc.set(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
      this.camera.updateMatrixWorld();
      _ray.setFromCamera(_ndc, this.camera);
      return _ray.ray.clone();
    },

    render() {
      if (!this.renderer) return;
      this.time = (performance.now() - this._t0) / 1000;
      shared.time.value = this.time;
      if (SG.LP) SG.LP.setTime(this.time);
      shared.equator.value.copy(this.equatorColor).multiplyScalar(this.hemi.intensity);
      const top = this.topScreen();
      if (top) {
        if (typeof top.render === 'function') top.render(this.renderer);
        else this.renderer.render(top.scene, top.camera);
        this._trackPerf();
        return;
      }
      this.sky.position.copy(this.camera.position);
      this.renderer.render(this.scene, this.camera);
      this._trackPerf();
    },
    _trackPerf() {
      const now = performance.now(), p = this._perf;
      if (this.autoQuality && p.last && now - this._t0 > 4000) {
        const dt = now - p.last;
        // 长帧也计入（截到 1 秒）；最近 8 帧有 5 帧超过 150 毫秒则立刻降一档
        p.acc += Math.min(dt, 1000); p.n++;
        p.slow = ((p.slow || 0) << 1 | (dt > 150 ? 1 : 0)) & 0xff;
        let k = 0; for (let b = p.slow; b; b >>= 1) k += b & 1;
        if (k >= 5) { this.degrade(); p.slow = 0; p.acc = 0; p.n = 0; }
        else if (p.n >= 120) { if (p.acc / p.n > 28) this.degrade(); p.acc = 0; p.n = 0; }
      }
      p.last = now;
    },
    // 降一档画质；已是最低时返回 false
    degrade() {
      if (this.maxPixelRatio > 1 && (window.devicePixelRatio || 1) > 1 && this.renderer.getPixelRatio() > 1) {
        this.maxPixelRatio = Math.max(1, Math.min(this.maxPixelRatio, window.devicePixelRatio || 1) - 0.25);
        this.resize();
        return true;
      }
      const sh = this.sun.shadow;
      if (sh.mapSize.x > 1024) {
        sh.mapSize.set(1024, 1024);
        if (sh.map) { sh.map.dispose(); sh.map = null; }
        return true;
      }
      return false;
    },

    // ----------------------------------------------------------- 氛围 --
    // 主场景的时间氛围（SG.LP.Mood，见 js/lowpoly.js）。各场景调用：
    //   SG.Gfx.setMood('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | 小时数) → mood.state
    //   SG.Gfx.addLamp([x,y,z], { color, intensity, range, nightOnly, halo, haloSize, flicker }) → lamp；removeLamp(lamp)；setLampLit(lamp, on)
    //   SG.Gfx.newMood(scene, opts) → 全屏场景（单挑、攻城等 pushScreen 的独立 scene）自己的 LP.Mood（自带太阳与天光）
    //   SG.Gfx.mood（LP.Mood）/ SG.Gfx.moodState()
    // 正午即原有画面（太阳、三色环境光、雾、天空、水面原值）；其他时刻按 Lanternvale 关键帧的比例缩放原有材质的光照，
    // LP 材质则直接得到 Lanternvale 的光照（金色时刻提亮、夜色调、暖色灯池）。阴影方向随太阳移动（setShadowFocus）。
    _initMood() {
      if (!SG.LP || !SG.LP.Mood) { this.mood = null; return; }
      const LP = SG.LP;
      LP.setOutputEncoded(true);
      this.mood = new LP.Mood(this.scene, {
        sun: this.sun, hemi: this.hemi, positionSun: false, baseDir: [SUN_DIR.x, SUN_DIR.y, SUN_DIR.z],
        legacy: {
          sunColor: SUN_COLOR, sunIntensity: SUN_INTENSITY * Math.PI * EXPOSURE_SUN, skyColor: AMB_SKY, groundColor: AMB_GROUND,
          hemiIntensity: Math.PI * EXPOSURE_AMB, equator: this.equatorColor, equatorBase: AMB_EQUATOR, shadowUniform: shared.shadowStrength, shadowBase: 0.55,
        },
        fog: { base: FOG_COLOR, background: true }, fogMax: 0.85, shadowStrength: 0.7, maxLamps: 6,
      });
      this.mood.set('noon');
      this.sunDir = SUN_DIR.clone();
    },
    setMood(what) {
      if (!this.mood) return null;
      this.mood.set(what);
      const d = this.mood.state.dir;
      this.sunDir = new THREE.Vector3(d[0], d[1], d[2]).normalize();
      this._moodExtras();
      return this.mood.state;
    },
    moodState() { return this.mood ? this.mood.state : null; },
    // 天空穹顶与水面跟随氛围（正午 = 原值）
    _moodExtras() {
      const st = this.mood && this.mood.state;
      if (!st) return;
      const noon = SG.LP.moodAt(12.5);
      const ratio = (a, b) => [0, 1, 2].map(k => (b[k] > 1e-4 ? a[k] / b[k] : 0));
      const rs = ratio(st.sunCol, noon.sunCol).map(v => v * st.sunI / noon.sunI), rk = ratio(st.sky, noon.sky).map(v => v * st.ambI / noon.ambI);
      const deep = st.deepK || 0, golden = st.golden || 0;
      if (this.sky && this._skyBase) {
        const u = this.sky.material.uniforms, b = this._skyBase, fog = st.fog || [b.horizon.r, b.horizon.g, b.horizon.b];
        u.uTop.value.setRGB(b.top.r * rk[0], b.top.g * rk[1], b.top.b * rk[2]).lerp(color(0.05, 0.07, 0.17), deep);
        u.uHorizon.value.setRGB(fog[0], fog[1], fog[2]);
        u.uBottom.value.setRGB(b.bottom.r * rk[0], b.bottom.g * rk[1], b.bottom.b * rk[2]);
        u.uSunColor.value.setRGB(b.sun.r * rs[0], b.sun.g * rs[1], b.sun.b * rs[2]).lerp(color(1, 0.72, 0.42), golden * 0.6).multiplyScalar(1 - 0.85 * deep);
        u.uSunDir.value.copy(this.sunDir || SUN_DIR);
      }
      if (this._water && this._waterBase) {
        const u = this._water.uniforms, b = this._waterBase;
        u.uSunColor.value.setRGB(b.sun.r * rs[0], b.sun.g * rs[1], b.sun.b * rs[2]);
        u.uAmbient.value.setRGB(b.amb.r * rk[0], b.amb.g * rk[1], b.amb.b * rk[2]);
        u.uSheenColor.value.setRGB(b.sheen.r * rs[0], b.sheen.g * rs[1], b.sheen.b * rs[2]).lerp(color(1, 0.78, 0.5), golden * 0.5);
        u.uSkyColor.value.copy(b.sky).lerp(color(...(st.fog || [b.sky.r, b.sky.g, b.sky.b])), 0.5 * Math.max(golden, deep));
        u.uDeep.value.copy(b.deep).multiplyScalar(1 - 0.55 * deep);
        u.uShallow.value.copy(b.shallow).multiplyScalar(1 - 0.55 * deep);
        u.uSunDir.value.copy(this.sunDir || SUN_DIR);
      }
    },
    addLamp(pos, opts) { return this.mood ? this.mood.addLamp(Array.isArray(pos) ? pos : [pos.x, pos.y, pos.z], opts) : null; },
    removeLamp(lamp) { if (this.mood && lamp) this.mood.removeLamp(lamp); },
    setLampLit(lamp, on) { if (this.mood && lamp) this.mood.setLampLit(lamp, on); },
    newMood(scene, opts) { return SG.LP && SG.LP.Mood ? new SG.LP.Mood(scene, opts) : null; },

    // ----------------------------------------------------------- 材质 --
    lowPoly() {
      if (!this._lowPoly) this._lowPoly = new LowPolyMaterial();
      return this._lowPoly;
    },
    newLowPoly() { return new LowPolyMaterial(); },
    water() {
      if (this._water) return this._water;
      const sunC = color(SUN_COLOR).multiplyScalar(SUN_INTENSITY * EXPOSURE_SUN);
      const amb = color(AMB_SKY).multiplyScalar(EXPOSURE_AMB);
      this._water = new THREE.ShaderMaterial({
        uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, {
          uAmp: { value: 0.08 },
          uAlpha: { value: 0.86 },
          uDeep: { value: color(0.07, 0.27, 0.48) },
          uShallow: { value: color(0.25, 0.7, 0.75) },
          uSunDir: { value: SUN_DIR.clone() },
          uSunColor: { value: sunC },
          uAmbient: { value: amb },
          uSheenDir: { value: new THREE.Vector3(0.3, 0.4, -0.85).normalize() },
          uSheenColor: { value: color(1, 0.93, 0.78) },
          uSkyColor: { value: color(0.8, 0.84, 0.86) },
          uRipple: { value: null },
        }]),
        vertexShader: WATER_VS, fragmentShader: WATER_FS,
        transparent: true, depthWrite: false, fog: true, vertexColors: true,
        extensions: { derivatives: true },
      });
      this._water.uniforms.uTime = shared.time;
      this._water.uniforms.uRipple.value = rippleTexture();
      this._waterBase = { sun: this._water.uniforms.uSunColor.value.clone(), amb: this._water.uniforms.uAmbient.value.clone(), sky: this._water.uniforms.uSkyColor.value.clone(), sheen: this._water.uniforms.uSheenColor.value.clone(), deep: this._water.uniforms.uDeep.value.clone(), shallow: this._water.uniforms.uShallow.value.clone() };
      if (this.mood && this.mood.state && this.mood.state.hour !== 12.5) this._moodExtras();
      return this._water;
    },
    // Unlit.shader：顶点无关，颜色 × 贴图，半透明，不写深度，双面，深度偏移 -1,-1
    unlit(c, texture, opacity) {
      const ca = colorAlpha(c);
      return new THREE.MeshBasicMaterial({
        color: ca.color, map: texture || null, transparent: true,
        opacity: opacity === undefined ? ca.a : opacity,
        depthWrite: false, side: THREE.DoubleSide, fog: false,
        polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1,
      });
    },
    // Additive.shader：柔光点 × 颜色，叠加混合。kind: 'mesh'（默认）| 'points' | 'sprite'
    additive(c, kind, size) {
      const ca = colorAlpha(c);
      const common = {
        color: ca.color, map: softDotTexture(), transparent: true, opacity: ca.a,
        blending: THREE.AdditiveBlending, depthWrite: false, fog: false,
      };
      if (kind === 'points') return new THREE.PointsMaterial(Object.assign(common, { size: size || 1, sizeAttenuation: true }));
      if (kind === 'sprite') return new THREE.SpriteMaterial(common);
      return new THREE.MeshBasicMaterial(Object.assign(common, { side: THREE.DoubleSide }));
    },
    get ringTexture() { return ringTexture(); },
    get softDotTexture() { return softDotTexture(); },
    shade, color, lerpColor,

    mesh(geometry, material, opts) {
      const o = opts || {};
      const m = new THREE.Mesh(geometry, material);
      m.castShadow = o.castShadow !== undefined ? o.castShadow : true;
      m.receiveShadow = o.receiveShadow !== undefined ? o.receiveShadow : true;
      return m;
    },
    // 释放一个对象树的几何体（共享材质不释放）
    disposeTree(obj) {
      if (!obj) return;
      obj.traverse(o => { if (o.geometry && !o.userData.sharedGeometry) o.geometry.dispose(); });
      if (obj.parent) obj.parent.remove(obj);
    },
  };

  // ------------------------------------------------------------- MeshBuilder --
  // 平面着色网格构建器：每个三角形独立顶点 + 面法线 + 顶点色。
  // 输入为 Unity 坐标（左手系，z 向北），内部转换为 three 坐标 (x, y, -z)。
  // 镜像会使环绕方向反转，因此逐三角形按 (a, c, b) 输出，使 C# 中的正面在 three 中仍为正面。
  class MeshBuilder {
    constructor() {
      this.pos = []; this.nor = []; this.col = []; this.uvs = [];
      this.M = null;          // 可选 THREE.Matrix4（Unity 坐标系下先变换再转换）
      this.hasUV = false;
    }
    get count() { return this.pos.length / 3; }
    clear() { this.pos.length = 0; this.nor.length = 0; this.col.length = 0; this.uvs.length = 0; }

    _p(p) {
      if (this.M) { _v.set(p.x, p.y, p.z).applyMatrix4(this.M); return { x: _v.x, y: _v.y, z: -_v.z }; }
      return { x: p.x, y: p.y, z: -p.z };
    }
    static _rgb(c) {
      if (c && typeof c.r === 'number') return c;
      return color(c);
    }
    _emit(A, B, C, c, uA, uB, uC) {
      // 法线 = (B-A)×(C-A)（此处 A,B,C 已是 three 坐标且为输出顺序）
      const ux = B.x - A.x, uy = B.y - A.y, uz = B.z - A.z;
      const vx = C.x - A.x, vy = C.y - A.y, vz = C.z - A.z;
      let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      const l = Math.sqrt(nx * nx + ny * ny + nz * nz);
      if (l > 1e-12) { nx /= l; ny /= l; nz /= l; } else { nx = 0; ny = 1; nz = 0; }
      const P = this.pos, N = this.nor, K = this.col, U = this.uvs;
      P.push(A.x, A.y, A.z, B.x, B.y, B.z, C.x, C.y, C.z);
      N.push(nx, ny, nz, nx, ny, nz, nx, ny, nz);
      K.push(c.r, c.g, c.b, c.r, c.g, c.b, c.r, c.g, c.b);
      U.push(uA[0], uA[1], uB[0], uB[1], uC[0], uC[1]);
    }
    tri(a, b, c, col) {
      const A = this._p(a), B = this._p(b), C = this._p(c);
      this._emit(A, C, B, MeshBuilder._rgb(col), UV00, UV01, UV10);
    }
    quad(a, b, c, d, col) { this.tri(a, b, c, col); this.tri(a, c, d, col); }
    // 带 UV 的平面四边形（用于透明贴图面片）：a(0,0) b(0,1) c(1,1) d(1,0)
    flatQuad(a, b, c, d, col) {
      this.hasUV = true;
      const k = MeshBuilder._rgb(col);
      const A = this._p(a), B = this._p(b), C = this._p(c), D = this._p(d);
      this._emit(A, C, B, k, UV00, UV11, UV01);
      this._emit(A, D, C, k, UV00, UV10, UV11);
    }
    box(center, size, col, topShade) {
      if (topShade === undefined) topShade = 0.08;
      col = MeshBuilder._rgb(col);
      const hx = size.x * 0.5, hy = size.y * 0.5, hz = size.z * 0.5, cx = center.x, cy = center.y, cz = center.z;
      const p0 = v3(cx - hx, cy - hy, cz - hz), p1 = v3(cx + hx, cy - hy, cz - hz), p2 = v3(cx + hx, cy - hy, cz + hz), p3 = v3(cx - hx, cy - hy, cz + hz);
      const q0 = v3(cx - hx, cy + hy, cz - hz), q1 = v3(cx + hx, cy + hy, cz - hz), q2 = v3(cx + hx, cy + hy, cz + hz), q3 = v3(cx - hx, cy + hy, cz + hz);
      this.quad(q0, q3, q2, q1, shade(col, topShade));
      this.quad(p0, q0, q1, p1, col);
      this.quad(p1, q1, q2, p2, col);
      this.quad(p2, q2, q3, p3, col);
      this.quad(p3, q3, q0, p0, col);
      this.quad(p0, p1, p2, p3, shade(col, -0.3));
    }
    // 棱柱 / 圆柱（低段数）
    cylinder(bottom, r0, r1, h, seg, col, cap) {
      if (cap === undefined) cap = true;
      col = MeshBuilder._rgb(col);
      const capCol = shade(col, 0.08);
      const top = v3(bottom.x, bottom.y + h, bottom.z);
      for (let i = 0; i < seg; i++) {
        const a0 = i * Math.PI * 2 / seg, a1 = (i + 1) * Math.PI * 2 / seg;
        const c0 = Math.cos(a0), s0 = Math.sin(a0), c1 = Math.cos(a1), s1 = Math.sin(a1);
        const b0 = v3(bottom.x + c0 * r0, bottom.y, bottom.z + s0 * r0), b1 = v3(bottom.x + c1 * r0, bottom.y, bottom.z + s1 * r0);
        const t0 = v3(bottom.x + c0 * r1, bottom.y + h, bottom.z + s0 * r1), t1 = v3(bottom.x + c1 * r1, bottom.y + h, bottom.z + s1 * r1);
        if (r1 > 0.0001) this.quad(b0, t0, t1, b1, col); else this.tri(b0, t0, b1, col);
        if (cap && r1 > 0.0001) this.tri(top, t1, t0, capCol);
      }
    }
    cone(bottom, r, h, seg, col) { this.cylinder(bottom, r, 0, h, seg, col, false); }
    // 低多边形球（八面体细分一次）
    blob(center, radius, col, seed) {
      col = MeshBuilder._rgb(col);
      const rnd = SG.SeededRandom(seed || 0);
      for (let f = 0; f < 8; f++) {
        const a = BLOB_PTS[BLOB_FACES[f][0]], b = BLOB_PTS[BLOB_FACES[f][1]], c = BLOB_PTS[BLOB_FACES[f][2]];
        const ab = nrm(a.x + b.x, a.y + b.y, a.z + b.z), bc = nrm(b.x + c.x, b.y + c.y, b.z + c.z), ca = nrm(c.x + a.x, c.y + a.y, c.z + a.z);
        const tris = [[a, ab, ca], [ab, b, bc], [ca, bc, c], [ab, bc, ca]];
        for (const tr of tris) {
          const k = 0.95 + rnd.nextDouble() * 0.1;
          const P = tr.map(p => v3(center.x + p.x * radius.x * k, center.y + p.y * radius.y * k, center.z + p.z * radius.z * k));
          this.tri(P[0], P[1], P[2], shade(col, (tr[0].y + tr[1].y + tr[2].y) * 0.04));
        }
      }
    }
    // 中式屋顶（第三版：凹曲屋面、檐口两端起翘、平滑法线，正脊两端加鸱吻）。bc 为檐口中心（Unity），w × d 为檐口外缘，h 为脊高
    chineseRoof(bc, w, d, h, col) {
      col = MeshBuilder._rgb(col);
      const rw = w * 0.22, ew = w * 0.5, ed = d * 0.5, NS = w > 0.7 ? 4 : w > 0.45 ? 3 : 2;
      // 四圈（檐口 → 两道中圈 → 正脊）：x 半宽、z 半深、高度、翼角起翘
      const R = [[ew, ed, h * 0.08, h * 0.16], [rw + (ew - rw) * 0.64, ed * 0.62, h * 0.27, h * 0.06], [rw + (ew - rw) * 0.3, ed * 0.3, h * 0.6, h * 0.015], [rw, 0, h, 0]];
      const ringPt = (k, side, t) => {
        // side 0 南（−z）1 东（+x）2 北（+z）3 西（−x）；t 沿边 0→1
        const r = R[k], lift = r[3] * Math.pow(Math.abs(2 * t - 1), 2.4), out = 1 + 0.06 * Math.pow(Math.abs(2 * t - 1), 3) * (k === 0 ? 1 : 0);
        let x, z;
        if (side === 0) { x = (-r[0] + 2 * r[0] * t) * out; z = -r[1] * out; }
        else if (side === 1) { x = r[0] * out; z = (-r[1] + 2 * r[1] * t) * out; }
        else if (side === 2) { x = (r[0] - 2 * r[0] * t) * out; z = r[1] * out; }
        else { x = -r[0] * out; z = (r[1] - 2 * r[1] * t) * out; }
        return v3(bc.x + x, bc.y + r[2] + lift, bc.z + z);
      };
      const dark = shade(col, -0.18), mid = shade(col, -0.06);
      for (let side = 0; side < 4; side++) {
        // 网格点 P[k][i]，各点法线为相邻面法线之和（同一坡面内平滑，戗脊处分开）
        const P = [], N = [];
        for (let k = 0; k < 4; k++) { P.push([]); N.push([]); for (let i = 0; i <= NS; i++) { P[k].push(ringPt(k, side, i / NS)); N[k].push(v3(0, 0, 0)); } }
        const outward = side === 0 ? v3(0, 0, -1) : side === 1 ? v3(1, 0, 0) : side === 2 ? v3(0, 0, 1) : v3(-1, 0, 0);
        const fn = (a, b, c) => { const n = new THREE.Vector3().subVectors(b, a).cross(new THREE.Vector3().subVectors(c, a)); if (n.y < 0) n.negate(); return n; };
        for (let k = 0; k < 3; k++) for (let i = 0; i < NS; i++) {
          const n = fn(P[k][i], P[k][i + 1], P[k + 1][i]).add(outward.clone().multiplyScalar(0.02));
          N[k][i].add(n); N[k][i + 1].add(n); N[k + 1][i].add(n); N[k + 1][i + 1].add(n);
        }
        for (let k = 0; k < 3; k++) for (let i = 0; i < NS; i++) {
          const c0 = k === 0 ? dark : k === 1 ? mid : col, c1 = k === 0 ? mid : col;
          const a = P[k][i], b = P[k][i + 1], c = P[k + 1][i + 1], dd = P[k + 1][i];
          const na = N[k][i].clone().normalize(), nb = N[k][i + 1].clone().normalize(), nc = N[k + 1][i + 1].clone().normalize(), nd = N[k + 1][i].clone().normalize();
          this.triN(a, b, c, na, nb, nc, c0, c0, c1);
          if (dd.distanceToSquared(c) > 1e-12) this.triN(a, c, dd, na, nc, nd, c0, c1, c1);
        }
        // 檐下（深色，略厚）
        const e0 = P[0][0], e1 = P[0][NS], down = v3(0, -1, 0), th = h * 0.05;
        const u0 = v3(e0.x, e0.y - th, e0.z), u1 = v3(e1.x, e1.y - th, e1.z);
        this.triN(e0, u1, e1, outward, outward, outward, shade(col, -0.45));
        this.triN(e0, u0, u1, outward, outward, outward, shade(col, -0.45));
        void down;
      }
      // 正脊 + 鸱吻（小屋顶只有正脊）
      const ridgeC = shade(col, -0.32);
      this.box(v3(bc.x, bc.y + h + h * 0.04, bc.z), v3(rw * 2 + h * 0.12, h * 0.1, Math.max(0.01, h * 0.09)), ridgeC);
      if (w > 0.5) for (const sx of [-1, 1]) {
        this.box(v3(bc.x + sx * (rw + h * 0.04), bc.y + h + h * 0.14, bc.z), v3(h * 0.08, h * 0.22, h * 0.08), ridgeC);
        this.box(v3(bc.x + sx * (rw + h * 0.0), bc.y + h + h * 0.26, bc.z), v3(h * 0.1, h * 0.06, h * 0.06), ridgeC);
      }
    }
    flag(pole, h, fw, fh, col) {
      col = MeshBuilder._rgb(col);
      this.box(v3(pole.x, pole.y + h * 0.5, pole.z), v3(0.04, h, 0.04), new THREE.Color(0.3, 0.22, 0.12));
      const tx = pole.x, ty = pole.y + h, tz = pole.z;
      const a = v3(tx + 0.02, ty, tz), b = v3(tx + fw, ty - fh * 0.1, tz);
      const c = v3(tx + fw, ty - fh * 1.1, tz), d = v3(tx + 0.02, ty - fh, tz);
      this.quad(a, b, c, d, col); this.quad(a, d, c, b, col);
    }

    // ---- 第三版：平滑网格（逐顶点法线，DESIGN-V3 §1）----
    // 法线向量（Unity 坐标）→ three 坐标（按 this.M 的旋转部分变换，z 取反）
    _n(n) {
      let x = n.x, y = n.y, z = n.z;
      if (this.M) {
        const e = this.M.elements;
        const tx = e[0] * x + e[4] * y + e[8] * z, ty = e[1] * x + e[5] * y + e[9] * z, tz = e[2] * x + e[6] * y + e[10] * z;
        x = tx; y = ty; z = tz;
      }
      const l = Math.sqrt(x * x + y * y + z * z) || 1;
      return { x: x / l, y: y / l, z: -z / l };
    }
    // 三角形 + 各顶点法线与颜色（Unity 坐标）。正反面按给定法线自动确定（几何法线与三个法线之和同向）
    triN(a, b, c, na, nb, nc, ca, cb, cc) {
      let A = this._p(a), B = this._p(b), C = this._p(c);
      let NA = this._n(na), NB = this._n(nb), NC = this._n(nc);
      ca = MeshBuilder._rgb(ca); cb = cb ? MeshBuilder._rgb(cb) : ca; cc = cc ? MeshBuilder._rgb(cc) : ca;
      const ux = B.x - A.x, uy = B.y - A.y, uz = B.z - A.z, vx = C.x - A.x, vy = C.y - A.y, vz = C.z - A.z;
      const gx = uy * vz - uz * vy, gy = uz * vx - ux * vz, gz = ux * vy - uy * vx;
      if (gx * (NA.x + NB.x + NC.x) + gy * (NA.y + NB.y + NC.y) + gz * (NA.z + NB.z + NC.z) < 0) {
        let t = B; B = C; C = t;
        t = NB; NB = NC; NC = t;
        t = cb; cb = cc; cc = t;
      }
      this.pos.push(A.x, A.y, A.z, B.x, B.y, B.z, C.x, C.y, C.z);
      this.nor.push(NA.x, NA.y, NA.z, NB.x, NB.y, NB.z, NC.x, NC.y, NC.z);
      this.col.push(ca.r, ca.g, ca.b, cb.r, cb.g, cb.b, cc.r, cc.g, cc.b);
      this.uvs.push(0, 0, 0, 1, 1, 0);
    }
    // 平滑椭球（二十面体细分 detail 次：0 = 20、1 = 80、2 = 320 个三角形）。
    // opts：seed、rough（起伏幅度，0–0.3）、top（顶面提亮，默认 0.06）、flat（y 低于该值处压平，用于石块、树冠底）、
    //       bottomShade（底部压暗，默认 0）
    sphere(center, radius, col, detail, opts) {
      const o = opts || {};
      const r = typeof radius === 'number' ? { x: radius, y: radius, z: radius } : radius;
      const ico = icosphere(detail === undefined ? 1 : detail);
      col = MeshBuilder._rgb(col);
      const n = ico.v.length / 3, P = new Float64Array(n * 3);
      const rough = o.rough || 0, s = (o.seed || 0) * 0.618;
      const flat = o.flat === undefined ? -2 : o.flat;
      for (let i = 0; i < n; i++) {
        const dx = ico.v[i * 3], dy = ico.v[i * 3 + 1], dz = ico.v[i * 3 + 2];
        let k = 1;
        if (rough) k += rough * (Math.sin(dx * 3.1 + s * 5.3) * Math.sin(dy * 2.7 + s * 2.1 + 1) * Math.sin(dz * 3.3 + s * 3.7 + 2) * 1.6 +
          Math.sin(dx * 6.7 + dz * 5.1 + s * 9.1) * 0.35);
        const y = dy < flat ? flat + (dy - flat) * 0.12 : dy;
        P[i * 3] = center.x + dx * r.x * k; P[i * 3 + 1] = center.y + y * r.y * k; P[i * 3 + 2] = center.z + dz * r.z * k;
      }
      const N = smoothNormals(P, ico.f, center);
      // smoothN（0–1）：法线向未起伏的椭球法线混合——轮廓仍有起伏，明暗却圆润（不再显出一块块小面）
      if (o.smoothN > 0) {
        const k = Math.min(1, o.smoothN);
        for (let i = 0; i < n; i++) {
          let ex = ico.v[i * 3] / r.x, ey = ico.v[i * 3 + 1] / r.y, ez = ico.v[i * 3 + 2] / r.z;
          const el = Math.hypot(ex, ey, ez) || 1;
          ex = N[i * 3] * (1 - k) + ex / el * k; ey = N[i * 3 + 1] * (1 - k) + ey / el * k; ez = N[i * 3 + 2] * (1 - k) + ez / el * k;
          const l = Math.hypot(ex, ey, ez) || 1;
          N[i * 3] = ex / l; N[i * 3 + 1] = ey / l; N[i * 3 + 2] = ez / l;
        }
      }
      const top = o.top === undefined ? 0.06 : o.top, bot = o.bottomShade || 0;
      const K = [];
      for (let i = 0; i < n; i++) {
        const dy = ico.v[i * 3 + 1];
        K.push(shade(col, dy * top - (dy < 0 ? -dy * bot : 0)));
      }
      const f = ico.f, pv = i => v3(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]), nv = i => v3(N[i * 3], N[i * 3 + 1], N[i * 3 + 2]);
      for (let q = 0; q < f.length; q += 3) this.triN(pv(f[q]), pv(f[q + 1]), pv(f[q + 2]), nv(f[q]), nv(f[q + 1]), nv(f[q + 2]), K[f[q]], K[f[q + 1]], K[f[q + 2]]);
    }
    // 旋转体（平滑）：profile = [[r, y, col?, sharp?], …]（由下而上，相对 base），seg 段。
    // sharp 的轮廓点两侧法线不平均（折角）。opts：capTop、capBottom、y0Shade（自下而上的明暗 [下, 上]）
    lathe(base, profile, seg, col, opts) {
      const o = opts || {};
      col = MeshBuilder._rgb(col);
      const np = profile.length;
      const ring = (k, a) => { const p = profile[k]; return v3(base.x + Math.cos(a) * p[0], base.y + p[1], base.z + Math.sin(a) * p[0]); };
      const tan = (i, j) => { const dr = profile[j][0] - profile[i][0], dy = profile[j][1] - profile[i][1]; const l = Math.hypot(dr, dy) || 1; return [dy / l, -dr / l]; };
      const nAt = (k, segIdx) => {
        // segIdx：所在的段（k 与 k+1 或 k−1 与 k）；sharp 点只取本段方向
        const own = segIdx === k ? tan(k, k + 1) : tan(k - 1, k);
        if (profile[k][3] || k === 0 || k === np - 1) return own;
        const a = tan(k - 1, k), b = tan(k, k + 1), x = a[0] + b[0], y = a[1] + b[1], l = Math.hypot(x, y) || 1;
        return [x / l, y / l];
      };
      const colAt = k => profile[k][2] ? MeshBuilder._rgb(profile[k][2]) : col;
      for (let k = 0; k + 1 < np; k++) {
        const n0 = nAt(k, k), n1 = nAt(k + 1, k), c0 = colAt(k), c1 = colAt(k + 1);
        if (profile[k][0] < 1e-5 && profile[k + 1][0] < 1e-5) continue;
        for (let i = 0; i < seg; i++) {
          const a0 = i * Math.PI * 2 / seg, a1 = (i + 1) * Math.PI * 2 / seg;
          const N = (n, a) => v3(Math.cos(a) * n[0], n[1], Math.sin(a) * n[0]);
          const p00 = ring(k, a0), p01 = ring(k, a1), p10 = ring(k + 1, a0), p11 = ring(k + 1, a1);
          if (profile[k][0] > 1e-5) this.triN(p00, p10, p01, N(n0, a0), N(n1, a0), N(n0, a1), c0, c1, c0);
          if (profile[k + 1][0] > 1e-5) this.triN(p01, p10, p11, N(n0, a1), N(n1, a0), N(n1, a1), c0, c1, c1);
        }
      }
      const cap = (k, up) => {
        const p = profile[k];
        if (!(p[0] > 1e-5)) return;
        const c = shade(colAt(k), up ? 0.06 : -0.2), ctr = v3(base.x, base.y + p[1], base.z), n = v3(0, up ? 1 : -1, 0);
        for (let i = 0; i < seg; i++) this.triN(ctr, ring(k, i * Math.PI * 2 / seg), ring(k, (i + 1) * Math.PI * 2 / seg), n, n, n, c);
      };
      if (o.capTop) cap(np - 1, true);
      if (o.capBottom) cap(0, false);
    }
    // 平滑圆柱 / 圆台（可带顶盖）
    smoothCylinder(bottom, r0, r1, h, seg, col, cap) {
      this.lathe(bottom, [[r0, 0], [r1, h]], seg, col, { capTop: cap !== false && r1 > 1e-5 });
    }
    // 沿折线的平滑管（树干、绳索）：pts 为 Unity 坐标点，radii 为各点半径
    tube(pts, radii, seg, col, capEnd) {
      col = MeshBuilder._rgb(col);
      const n = pts.length;
      if (n < 2) return;
      const rings = [];
      let ref = v3(1, 0, 0);
      for (let i = 0; i < n; i++) {
        const a = pts[Math.max(0, i - 1)], b = pts[Math.min(n - 1, i + 1)];
        const t = v3(b.x - a.x, b.y - a.y, b.z - a.z).normalize();
        if (Math.abs(t.dot(ref)) > 0.9) ref = v3(0, 0, 1);
        const u = new THREE.Vector3().crossVectors(t, ref).normalize(), w = new THREE.Vector3().crossVectors(t, u).normalize();
        const R = [], Nn = [];
        for (let j = 0; j < seg; j++) {
          const ang = j * Math.PI * 2 / seg, ca = Math.cos(ang), sa = Math.sin(ang);
          const nx = u.x * ca + w.x * sa, ny = u.y * ca + w.y * sa, nz = u.z * ca + w.z * sa;
          Nn.push(v3(nx, ny, nz));
          R.push(v3(pts[i].x + nx * radii[i], pts[i].y + ny * radii[i], pts[i].z + nz * radii[i]));
        }
        rings.push({ R, N: Nn });
      }
      for (let i = 0; i + 1 < n; i++)
        for (let j = 0; j < seg; j++) {
          const j1 = (j + 1) % seg, A = rings[i], B = rings[i + 1];
          this.triN(A.R[j], B.R[j], A.R[j1], A.N[j], B.N[j], A.N[j1], col);
          this.triN(A.R[j1], B.R[j], B.R[j1], A.N[j1], B.N[j], B.N[j1], col);
        }
      if (capEnd) {
        const L = rings[n - 1], c = pts[n - 1], tn = v3(c.x - pts[n - 2].x, c.y - pts[n - 2].y, c.z - pts[n - 2].z).normalize();
        for (let j = 0; j < seg; j++) this.triN(c, L.R[j], L.R[(j + 1) % seg], tn, tn, tn, shade(col, 0.05));
      }
    }

    toGeometry() {
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
      g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nor, 3));
      g.setAttribute('color', new THREE.Float32BufferAttribute(this.col, 3));
      if (this.hasUV) g.setAttribute('uv', new THREE.Float32BufferAttribute(this.uvs, 2));
      g.computeBoundingBox();
      g.computeBoundingSphere();
      return g;
    }
  }
  const UV00 = [0, 0], UV10 = [1, 0], UV01 = [0, 1], UV11 = [1, 1];
  function v3(x, y, z) { return new THREE.Vector3(x, y, z); }
  // 单位二十面体细分（缓存）：v = 顶点（单位向量），f = 三角形顶点下标
  const _ico = [];
  function icosphere(detail) {
    detail = Math.max(0, Math.min(3, detail | 0));
    if (_ico[detail]) return _ico[detail];
    const t = (1 + Math.sqrt(5)) / 2;
    let V = [[-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]]
      .map(p => { const l = Math.hypot(p[0], p[1], p[2]); return [p[0] / l, p[1] / l, p[2] / l]; });
    let F = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
      3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
    for (let d = 0; d < detail; d++) {
      const mid = new Map(), F2 = [];
      const m = (a, b) => {
        const k = a < b ? a * 4096 + b : b * 4096 + a;
        let i = mid.get(k);
        if (i === undefined) {
          const p = V[a], q = V[b], x = p[0] + q[0], y = p[1] + q[1], z = p[2] + q[2], l = Math.hypot(x, y, z);
          i = V.length; V.push([x / l, y / l, z / l]); mid.set(k, i);
        }
        return i;
      };
      for (let q = 0; q < F.length; q += 3) {
        const a = F[q], b = F[q + 1], c = F[q + 2], ab = m(a, b), bc = m(b, c), ca = m(c, a);
        F2.push(a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca);
      }
      F = F2;
    }
    const v = new Float32Array(V.length * 3);
    V.forEach((p, i) => { v[i * 3] = p[0]; v[i * 3 + 1] = p[1]; v[i * 3 + 2] = p[2]; });
    _ico[detail] = { v, f: Uint16Array.from(F) };
    return _ico[detail];
  }
  // 索引网格的平滑法线（面法线按面积累加），朝离开 center 的方向
  function smoothNormals(P, F, center) {
    const N = new Float64Array(P.length);
    for (let q = 0; q < F.length; q += 3) {
      const a = F[q] * 3, b = F[q + 1] * 3, c = F[q + 2] * 3;
      const ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
      let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      const ox = (P[a] + P[b] + P[c]) / 3 - center.x, oy = (P[a + 1] + P[b + 1] + P[c + 1]) / 3 - center.y, oz = (P[a + 2] + P[b + 2] + P[c + 2]) / 3 - center.z;
      if (nx * ox + ny * oy + nz * oz < 0) { nx = -nx; ny = -ny; nz = -nz; }
      for (const i of [a, b, c]) { N[i] += nx; N[i + 1] += ny; N[i + 2] += nz; }
    }
    for (let i = 0; i < N.length; i += 3) { const l = Math.hypot(N[i], N[i + 1], N[i + 2]) || 1; N[i] /= l; N[i + 1] /= l; N[i + 2] /= l; }
    return N;
  }
  function nrm(x, y, z) { const l = Math.sqrt(x * x + y * y + z * z) || 1; return { x: x / l, y: y / l, z: z / l }; }
  // up, down, left, right, forward, back（Unity）
  const BLOB_PTS = [{ x: 0, y: 1, z: 0 }, { x: 0, y: -1, z: 0 }, { x: -1, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, { x: 0, y: 0, z: 1 }, { x: 0, y: 0, z: -1 }];
  const BLOB_FACES = [[0, 4, 3], [0, 3, 5], [0, 5, 2], [0, 2, 4], [1, 3, 4], [1, 5, 3], [1, 2, 5], [1, 4, 2]];

  // ------------------------------------------------------------- 常用模型 --
  const C = (r, g, b) => new THREE.Color(r, g, b);
  const addv = (p, x, y, z) => v3(p.x + x, p.y + y, p.z + z);
  const Models = {
    // 第三版：平滑树干与圆润树冠（阔叶约 92、针叶约 72 个三角形）
    tree(mb, p, s, leaf, pine, seed) {
      leaf = MeshBuilder._rgb(leaf);
      mb.lathe(p, [[0.065 * s, 0], [0.045 * s, 0.4 * s]], 6, C(0.38, 0.27, 0.16));
      if (pine) {
        mb.lathe(addv(p, 0, 0.22 * s, 0), [[0, 0.06 * s], [0.44 * s, 0, null, true], [0.2 * s, 0.36 * s], [0, 0.66 * s]], 10, shade(leaf, -0.04));
        mb.lathe(addv(p, 0, 0.6 * s, 0), [[0, 0.05 * s], [0.32 * s, 0, null, true], [0.13 * s, 0.32 * s], [0, 0.58 * s]], 10, shade(leaf, 0.08));
      } else mb.sphere(addv(p, 0, 0.66 * s, 0), v3(0.43 * s, 0.41 * s, 0.43 * s), leaf, 1, { seed: seed || 0, rough: 0.07, bottomShade: 0.25 });
    },

    // 城池：城墙、角楼、主殿、旗帜。size 约为占地半径。返回 BufferGeometry
    city(size, banner, capital) {
      const mb = new MeshBuilder();
      Models.cityInto(mb, size, banner, capital);
      return mb.toGeometry();
    },
    // 同上，但写入已有的 MeshBuilder（用于合并网格）
    cityInto(mb, size, banner, capital) {
      const stone = C(0.72, 0.69, 0.62);
      const wallH = 0.5 * size;
      const r = size;
      mb.box(v3(0, 0.05, 0), v3(r * 2.3, 0.1, r * 2.3), C(0.62, 0.58, 0.48));
      // 城墙
      const t = 0.22 * size;
      mb.box(v3(0, wallH / 2, -r), v3(r * 2, wallH, t), stone);
      mb.box(v3(0, wallH / 2, r), v3(r * 2, wallH, t), stone);
      mb.box(v3(-r, wallH / 2, 0), v3(t, wallH, r * 2), stone);
      mb.box(v3(r, wallH / 2, 0), v3(t, wallH, r * 2), stone);
      // 垛口
      for (let i = -3; i <= 3; i++) {
        const x = i * r / 3.5;
        mb.box(v3(x, wallH + 0.06 * size, -r), v3(0.14 * size, 0.12 * size, t * 1.05), shade(stone, 0.06));
        mb.box(v3(x, wallH + 0.06 * size, r), v3(0.14 * size, 0.12 * size, t * 1.05), shade(stone, 0.06));
      }
      // 城门楼
      mb.box(v3(0, wallH + 0.22 * size, -r), v3(0.7 * size, 0.36 * size, 0.36 * size), C(0.62, 0.2, 0.16));
      mb.chineseRoof(v3(0, wallH + 0.4 * size, -r), 1.0 * size, 0.6 * size, 0.32 * size, C(0.22, 0.26, 0.34));
      mb.box(v3(0, 0.2 * size, -r - 0.01), v3(0.3 * size, 0.4 * size, t * 1.1), C(0.18, 0.12, 0.08));
      // 角楼
      for (const cx of [-1, 1])
        for (const cz of [-1, 1]) {
          const p = v3(cx * r, 0, cz * r);
          mb.box(addv(p, 0, wallH * 0.65, 0), v3(t * 1.8, wallH * 1.3, t * 1.8), shade(stone, -0.05));
          mb.chineseRoof(addv(p, 0, wallH * 1.3, 0), t * 2.6, t * 2.6, 0.22 * size, C(0.24, 0.28, 0.36));
        }
      // 城内建筑
      const hall = C(0.66, 0.22, 0.18);
      mb.box(v3(0, 0.25 * size, 0.15 * size), v3(0.9 * size, 0.5 * size, 0.6 * size), hall);
      mb.chineseRoof(v3(0, 0.5 * size, 0.15 * size), 1.3 * size, 0.95 * size, 0.42 * size, capital ? C(0.85, 0.65, 0.2) : C(0.25, 0.3, 0.38));
      if (capital) {
        mb.box(v3(0, 0.82 * size, 0.15 * size), v3(0.6 * size, 0.3 * size, 0.4 * size), hall);
        mb.chineseRoof(v3(0, 0.97 * size, 0.15 * size), 0.9 * size, 0.65 * size, 0.34 * size, C(0.85, 0.65, 0.2));
      }
      for (let i = 0; i < 4; i++) {
        const p = v3((i % 2 === 0 ? -0.55 : 0.55) * size, 0, (i < 2 ? -0.4 : 0.6) * size);
        mb.box(addv(p, 0, 0.14 * size, 0), v3(0.36 * size, 0.28 * size, 0.3 * size), C(0.86, 0.8, 0.68));
        mb.chineseRoof(addv(p, 0, 0.28 * size, 0), 0.5 * size, 0.42 * size, 0.18 * size, C(0.3, 0.32, 0.36));
      }
      return mb;
    },

    // 一名士兵（约 0.5 高）
    soldier(mb, p, team, spear, s) {
      if (s === undefined) s = 1;
      team = MeshBuilder._rgb(team);
      const at = (x, y, z) => v3(p.x + x * s, p.y + y * s, p.z + z * s);
      const sz = (x, y, z) => v3(x * s, y * s, z * s);
      const skin = C(0.95, 0.8, 0.62);
      mb.box(at(-0.045, 0.09, 0), sz(0.06, 0.18, 0.07), C(0.25, 0.2, 0.18));
      mb.box(at(0.045, 0.09, 0), sz(0.06, 0.18, 0.07), C(0.25, 0.2, 0.18));
      mb.box(at(0, 0.27, 0), sz(0.18, 0.2, 0.11), team);
      mb.box(at(0, 0.3, 0.002), sz(0.19, 0.05, 0.115), shade(team, -0.35));
      mb.box(at(0, 0.43, 0), sz(0.11, 0.11, 0.11), skin);
      mb.cone(at(0, 0.48, 0), 0.09 * s, 0.07 * s, 6, C(0.45, 0.45, 0.5));
      if (spear) {
        mb.box(at(0.12, 0.38, 0), sz(0.02, 0.62, 0.02), C(0.4, 0.3, 0.18));
        mb.cone(at(0.12, 0.69, 0), 0.025 * s, 0.08 * s, 4, C(0.85, 0.85, 0.9));
      }
    },

    // 武将：骑马、披风、军旗
    commander(mb, p, team, s) {
      if (s === undefined) s = 1;
      team = MeshBuilder._rgb(team);
      const at = (x, y, z) => v3(p.x + x * s, p.y + y * s, p.z + z * s);
      const sz = (x, y, z) => v3(x * s, y * s, z * s);
      const horse = C(0.45, 0.3, 0.2);
      mb.box(at(0, 0.3, 0), sz(0.18, 0.18, 0.46), horse);
      mb.box(at(0, 0.44, 0.24), sz(0.1, 0.2, 0.12), horse);
      mb.box(at(0, 0.5, 0.3), sz(0.08, 0.08, 0.14), shade(horse, -0.1));
      for (const lx of [-0.06, 0.06]) for (const lz of [-0.17, 0.17])
        mb.box(at(lx, 0.11, lz), sz(0.05, 0.22, 0.05), shade(horse, -0.2));
      mb.box(at(0, 0.55, 0), sz(0.2, 0.22, 0.13), team);
      mb.box(at(0, 0.55, -0.08), sz(0.22, 0.26, 0.03), shade(team, -0.35));
      mb.box(at(0, 0.72, 0), sz(0.12, 0.12, 0.12), C(0.95, 0.8, 0.62));
      mb.cone(at(0, 0.77, 0), 0.1 * s, 0.1 * s, 6, C(0.85, 0.7, 0.25));
      mb.box(at(0, 0.88, 0), sz(0.02, 0.08, 0.02), C(0.85, 0.2, 0.15));
      mb.flag(at(-0.16, 0.4, -0.1), 0.9 * s, 0.36 * s, 0.28 * s, team);
    },
  };

  // ======================================================== 第三版：地形材质 --
  // 程序化细节纹理（可平铺，256²，RGBA，带 mipmap）——全部在代码中生成，不载入素材：
  //   A.r 草地明暗（簇状噪声）  A.g 泥土 / 碎石  A.b 沙纹  A.a 柔和噪声（雪面、过渡边缘）
  //   B.r 岩石明暗（层理 + 裂隙 + 脊状噪声）  B.g 大尺度变化  B.b 农田地块编号（0 = 田埂小路）  B.a 犁沟
  const TEX_N = 256;
  function periodicNoise(N, P, rnd) {
    const lat = new Float32Array(P * P);
    for (let i = 0; i < lat.length; i++) lat[i] = rnd.nextDouble();
    const out = new Float32Array(N * N), cell = N / P;
    for (let y = 0; y < N; y++) {
      const gy = y / cell, iy = Math.floor(gy);
      let fy = gy - iy; fy = fy * fy * (3 - 2 * fy);
      const y0 = (iy % P) * P, y1 = ((iy + 1) % P) * P;
      for (let x = 0; x < N; x++) {
        const gx = x / cell, ix = Math.floor(gx);
        let fx = gx - ix; fx = fx * fx * (3 - 2 * fx);
        const x0 = ix % P, x1 = (ix + 1) % P;
        const a = lat[y0 + x0], b = lat[y0 + x1], c = lat[y1 + x0], d = lat[y1 + x1];
        out[y * N + x] = (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
      }
    }
    return out;
  }
  function* fbmTex(N, periods, amps, seed, out) {
    const rnd = SG.SeededRandom(seed), acc = new Float32Array(N * N);
    for (let o = 0; o < periods.length; o++) {
      const n = periodicNoise(N, periods[o], rnd);
      for (let i = 0; i < acc.length; i++) acc[i] += n[i] * amps[o];
      yield;
    }
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < acc.length; i++) { lo = Math.min(lo, acc[i]); hi = Math.max(hi, acc[i]); }
    for (let i = 0; i < acc.length; i++) acc[i] = (acc[i] - lo) / Math.max(1e-6, hi - lo);
    out.v = acc;
  }
  // 可平铺的 Worley 噪声：f1 = 到最近特征点的距离，f2 − f1（裂隙 / 卵石边缘）
  function* worleyTex(N, P, seed, out) {
    const rnd = SG.SeededRandom(seed), cell = N / P;
    const fx = new Float32Array(P * P), fy = new Float32Array(P * P);
    for (let i = 0; i < P * P; i++) { fx[i] = rnd.nextDouble(); fy[i] = rnd.nextDouble(); }
    const f1 = new Float32Array(N * N), f21 = new Float32Array(N * N);
    for (let y = 0; y < N; y++) {
      const cy = Math.floor(y / cell);
      for (let x = 0; x < N; x++) {
        const cx = Math.floor(x / cell);
        let d1 = 1e9, d2 = 1e9;
        for (let j = -1; j <= 1; j++) for (let i = -1; i <= 1; i++) {
          const gx = cx + i, gy = cy + j, k = ((gy + P) % P) * P + ((gx + P) % P);
          const px = (gx + fx[k]) * cell, py = (gy + fy[k]) * cell;
          const d = Math.hypot(px - x, py - y);
          if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
        }
        f1[y * N + x] = d1 / cell; f21[y * N + x] = (d2 - d1) / cell;
      }
      if ((y & 31) === 31) yield;
    }
    out.f1 = f1; out.f21 = f21;
  }
  function makeDataTex(data, N) {
    const t = new THREE.DataTexture(data, N, N, THREE.RGBAFormat);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.magFilter = THREE.LinearFilter;
    t.minFilter = THREE.LinearMipmapLinearFilter;
    t.generateMipmaps = true;
    if (Gfx.renderer) t.anisotropy = Math.min(4, Gfx.renderer.capabilities.getMaxAnisotropy());
    t.needsUpdate = true;
    return t;
  }
  let _detail = null;
  // 生成器（可分片）；完成后 Gfx.terrainDetail() 直接返回
  function* terrainDetailGen() {
    if (_detail) return _detail;
    const N = TEX_N, NN = N * N;
    const g = {}, m = {}, s = {}, r = {}, w = {}, w2 = {}, lo = {};
    yield* fbmTex(N, [4, 8, 16, 32, 64, 128], [0.1, 0.16, 0.2, 0.2, 0.18, 0.16], 101, g);
    yield* fbmTex(N, [2, 4, 8, 16], [0.4, 0.3, 0.2, 0.1], 202, lo);
    yield* fbmTex(N, [8, 16, 32], [0.5, 0.3, 0.2], 303, m);
    yield* worleyTex(N, 22, 404, w);
    yield* fbmTex(N, [4, 8, 16, 32, 64], [0.32, 0.26, 0.2, 0.14, 0.08], 505, r);
    yield* worleyTex(N, 16, 606, w2);
    yield* fbmTex(N, [4, 8], [0.6, 0.4], 707, s);
    const A = new Uint8Array(NN * 4), B = new Uint8Array(NN * 4);
    const b8 = v => Math.max(0, Math.min(255, Math.round(v * 255)));
    for (let y = 0; y < N; y++) {
      for (let x = 0; x < N; x++) {
        const i = y * N + x, o = i * 4;
        // 草地：细噪声 + 簇（向两极拉开）
        let gr = g.v[i];
        gr = 0.5 + (gr - 0.5) * 1.6;
        A[o] = b8(gr);
        // 泥土（第三版修订）：以 fBm 为主，卵石只是很淡的、经噪声扭曲的斑点，不再有 Worley 的多边形轮廓线（远看像铺石路）
        const px = (x + Math.round((s.v[i] - 0.5) * 24) + N) % N, py = (y + Math.round((lo.v[i] - 0.5) * 24) + N) % N;
        const peb = Math.min(1, w.f1[py * N + px] * 1.3);
        A[o + 1] = b8(0.22 + 0.34 * m.v[i] + 0.3 * g.v[i] + 0.12 * (1 - peb * peb));
        // 沙纹（第三版修订）：三个方向的整数周期波纹（可平铺），按低频噪声混合——各处波纹方向不同，并用噪声扭曲
        const wa = s.v[i], wb = lo.v[((y + 97) % N) * N + (x + 141) % N];
        const k1 = Math.max(0, 1 - Math.abs(wa - 0.25) * 3), k2 = Math.max(0, 1 - Math.abs(wa - 0.6) * 3), k3 = Math.max(0, 1 - Math.abs(wb - 0.55) * 2.6);
        const kw = k1 + k2 + k3 + 1e-3, warp = (m.v[i] - 0.5) * 2.4;
        const r1 = Math.sin(((x + y) / N * 14 + warp) * Math.PI * 2), r2 = Math.sin(((x - 2 * y) / N * 9 + warp) * Math.PI * 2), r3 = Math.sin(((3 * x + y) / N * 6 + warp) * Math.PI * 2);
        const rip = 0.5 + 0.5 * (r1 * k1 + r2 * k2 + r3 * k3) / kw;
        A[o + 2] = b8(0.25 + 0.45 * rip * rip + 0.3 * g.v[i]);
        A[o + 3] = b8(lo.v[i]);
        // 岩石：脊状噪声 + 层理 + 颗粒 + 细裂隙（裂隙网按噪声扭曲，不成规则多边形）
        const ridge = 1 - Math.abs(r.v[i] * 2 - 1);
        const strata = 0.5 + 0.5 * Math.sin((y / N * 9 + r.v[i] * 1.6) * Math.PI * 2);
        let rock = 0.22 + 0.4 * ridge * ridge + 0.14 * strata + 0.2 * g.v[i] + 0.1 * m.v[i];
        const wx = (x + Math.round((m.v[i] - 0.5) * 40) + N) % N, wy = (y + Math.round((lo.v[i] - 0.5) * 40) + N) % N;
        const cr = w2.f21[wy * N + wx];
        if (cr < 0.04) rock *= 0.78 + 0.22 * cr / 0.04;
        B[o] = b8(rock);
        B[o + 1] = b8(lo.v[(i * 7) % NN] * 0.5 + m.v[i] * 0.5);
      }
      if ((y & 63) === 63) yield;
    }
    // 农田：按行切成田块（行高、田宽随机，可平铺），田埂编号 0；犁沟方向逐块随机（周期 8 像素，整除 256）
    const rnd = SG.SeededRandom(808);
    const cut = (total, min, max) => { const a = []; let t = 0; while (t < total) { const v = min + rnd.nextDouble() * (max - min); a.push(v); t += v; } const k = total / t; let c = 0; return a.map(v => (c += v * k)); };
    const rows = cut(N, 34, 84);
    let r0 = 0;
    for (let ri = 0; ri < rows.length; ri++) {
      const r1 = rows[ri], shift = Math.floor(rnd.nextDouble() * N);
      const cols = cut(N, 30, 96);
      const ids = cols.map(() => 0.06 + rnd.nextDouble() * 0.94), dirs = cols.map(() => rnd.nextDouble() < 0.5);
      for (let y = Math.floor(r0); y < Math.floor(r1); y++) {
        for (let xx = 0; xx < N; xx++) {
          const x = (xx + shift) % N;
          let c0 = 0, k = 0;
          while (k < cols.length - 1 && cols[k] <= xx) { c0 = cols[k]; k++; }
          const edge = Math.min(xx - c0, cols[k] - xx, y - r0, r1 - y);
          const o = (y * N + x) * 4;
          const furrow = 0.5 + 0.5 * Math.sin(((dirs[k] ? x : y) / 8) * Math.PI * 2);
          B[o + 2] = edge < 1.6 ? 0 : b8(ids[k]);
          B[o + 3] = b8(0.55 + 0.45 * furrow * (0.7 + 0.3 * g.v[y * N + x]));
        }
      }
      r0 = r1;
      yield;
    }
    _detail = { a: makeDataTex(A, N), b: makeDataTex(B, N) };
    return _detail;
  }

  // 地形着色器（在 LowPoly 光照之上）：按坡度 / 高度 / 群落混合草地、干草、泥土、农田、沙、岩石（三平面投影）、雪与海床，
  // 再乘以凹凸遮蔽。两种取数方式：
  //   maps 版（战略地图）：法线 / 高度、群落色、材质权重来自地图数据纹理（js/map-view.js 生成），几何体只需位置与法线；
  //   顶点版（其他场景可用）：顶点色 = 群落底色，attribute vec4 aMat = (沙, 岩石倾向, 雪, 1 − 农田)（缺省 (0,0,0,1) = 纯草地）。
  const TERRAIN_VS_PARS = 'varying vec3 vSgW;\nvarying vec3 vSgNW;\n#ifndef SG_TERRAIN_MAPS\nattribute vec4 aMat;\nvarying vec4 vSgMat;\n#endif\n';
  const TERRAIN_VS_MAIN = '#include <project_vertex>\n\tvSgW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n\tvSgNW = normalize( mat3( modelMatrix ) * objectNormal );\n#ifndef SG_TERRAIN_MAPS\n\tvSgMat = aMat;\n#endif\n';
  const TERRAIN_FS_PARS = `
uniform sampler2D sgDetA;
uniform sampler2D sgDetB;
varying vec3 vSgW;
varying vec3 vSgNW;
#ifdef SG_TERRAIN_MAPS
uniform sampler2D sgMapN;
uniform sampler2D sgMapC;
uniform sampler2D sgMapM;
uniform sampler2D sgOutC;
uniform sampler2D sgOutM;
uniform vec4 sgXfN;
uniform vec4 sgXfC;
uniform vec4 sgXfO;
uniform vec4 sgInner;
#else
varying vec4 vSgMat;
#endif
vec3 sgCrop( float id ) {
  vec3 c = vec3( 0.42, 0.57, 0.23 );
  c = mix( c, vec3( 0.75, 0.66, 0.31 ), step( 0.24, id ) );
  c = mix( c, vec3( 0.53, 0.41, 0.28 ), step( 0.42, id ) );
  c = mix( c, vec3( 0.33, 0.49, 0.2 ), step( 0.56, id ) );
  c = mix( c, vec3( 0.64, 0.62, 0.3 ), step( 0.72, id ) );
  c = mix( c, vec3( 0.47, 0.6, 0.26 ), step( 0.87, id ) );
  return c;
}
`;
  const TERRAIN_FS_ALBEDO = `
	vec2 sgP = vec2( vSgW.x, - vSgW.z );
	float sgH = vSgW.y;
	vec3 sgTint;
	vec4 sgMat;
	float sgForest = 0.0;
	float sgAO = 0.5;
	vec3 sgN = normalize( vSgNW );
	float sgNearW = 1.0;   // 第三版修订：低地是否靠近水面（海滩 / 河岸沙只在离水约 1 单位内出现；低平的岛屿内陆仍是草地）
#ifdef SG_TERRAIN_MAPS
	{
		vec2 e = min( sgP - sgInner.xy, sgInner.zw - sgP );
		float wI = clamp( min( e.x, e.y ) * 0.2, 0.0, 1.0 );
		sgTint = vec3( 0.0 ); sgMat = vec4( 0.0 );
		if ( wI < 1.0 ) {
			vec2 uvO = sgP * sgXfO.zw + sgXfO.xy;
			sgTint = texture2D( sgOutC, uvO ).rgb * ( 1.0 - wI );
			sgMat = texture2D( sgOutM, uvO ) * ( 1.0 - wI );
		}
		if ( wI > 0.0 ) {
			vec2 uvC = sgP * sgXfC.zw + sgXfC.xy;
			vec4 ic = texture2D( sgMapC, uvC );
			vec4 tn = texture2D( sgMapN, sgP * sgXfN.zw + sgXfN.xy );
			vec3 n2 = vec3( tn.r * 2.0 - 1.0, 0.0, tn.g * 2.0 - 1.0 );
			n2.y = sqrt( max( 0.0, 1.0 - dot( n2.xz, n2.xz ) ) );
			sgTint += ic.rgb * wI;
			sgMat += texture2D( sgMapM, uvC ) * wI;
			sgForest = ic.a * wI;
			sgAO = mix( 0.5, tn.b, wI );
			sgN = normalize( mix( sgN, n2, wI ) );
			if ( sgH < 0.6 ) {
				// 高度贴图（A = (h + 4) / 8）在四个方向、两个距离上取最低处：附近有水（h < 0.02）才允许沙滩
				vec2 uvN = sgP * sgXfN.zw + sgXfN.xy, d1 = vec2( 1.1, 0.0 ) * sgXfN.zw, d2 = vec2( 0.0, 1.1 ) * sgXfN.zw;
				float a1 = min( min( texture2D( sgMapN, uvN + d1 ).a, texture2D( sgMapN, uvN - d1 ).a ), min( texture2D( sgMapN, uvN + d2 ).a, texture2D( sgMapN, uvN - d2 ).a ) );
				float a2 = min( min( texture2D( sgMapN, uvN + d1 * 0.5 ).a, texture2D( sgMapN, uvN - d1 * 0.5 ).a ), min( texture2D( sgMapN, uvN + d2 * 0.5 ).a, texture2D( sgMapN, uvN - d2 * 0.5 ).a ) );
				float hmin = min( a1, a2 ) * 8.0 - 4.0;
				sgNearW = mix( 1.0, 1.0 - smoothstep( 0.03, 0.2, hmin ), wI );
			}
		}
	}
#else
	sgTint = diffuseColor.rgb;
	sgMat = vSgMat;
#endif
	vec4 sgDA = texture2D( sgDetA, sgP * 0.23 );
	vec4 sgDM = texture2D( sgDetA, sgP * 0.047 + vec2( 0.31, 0.67 ) );
	vec4 sgDF = texture2D( sgDetA, sgP * 0.93 + vec2( 0.53, 0.19 ) );
	float sgSlope = 1.0 - sgN.y;
	// 第三版修订：近景（镜头 18 单位内满、40 单位外无）多一级细节（土粒、草簇）、田里的垄行
	float sgNear = 1.0 - smoothstep( 18.0, 40.0, distance( cameraPosition, vSgW ) );
	// 草地：三级细节明暗（近处可见草簇纹理）+ 大尺度的干湿斑块 + 林下压暗
	vec3 sgCol = sgTint * ( 0.76 + 0.5 * ( sgDA.r * 0.36 + sgDF.r * 0.28 + sgDM.r * 0.36 ) );
	if ( sgNear > 0.01 ) { vec4 sgDN = texture2D( sgDetA, sgP * 3.1 + vec2( 0.7, 0.2 ) ); sgCol *= 1.0 + ( sgDN.r - 0.5 ) * 0.24 * sgNear; }
	// 草色略降饱和，并按大尺度噪声做冷暖斑块（不再是一整片饱和的黄绿）
	sgCol = mix( vec3( dot( sgCol, vec3( 0.3, 0.59, 0.11 ) ) ), sgCol, 0.9 ) * mix( vec3( 1.035, 1.0, 0.93 ), vec3( 0.96, 1.0, 1.04 ), sgDM.b );
	sgCol = mix( sgCol, sgCol * vec3( 1.1, 1.03, 0.8 ), smoothstep( 0.5, 0.78, sgDM.g ) * 0.55 );
	sgCol = mix( sgCol, sgCol * vec3( 0.78, 0.86, 0.74 ), smoothstep( 0.55, 0.85, sgDM.a ) * 0.6 );
	sgCol *= 1.0 - 0.3 * sgForest;
	// 裸土：陡坡、干旱区的斑块
	float sgDry = clamp( ( sgTint.r - sgTint.g * 0.85 ) * 4.0 + 0.3, 0.0, 1.0 );
	float sgDirtW = smoothstep( 0.66, 0.9, sgDM.a * 0.62 + sgSlope * 0.95 + sgDry * 0.2 + ( sgDA.g - 0.5 ) * 0.2 );
	vec3 sgDirt = mix( vec3( 0.48, 0.39, 0.28 ), sgTint * vec3( 0.95, 0.85, 0.7 ), 0.35 ) * ( 0.78 + 0.44 * sgDA.g );
	sgCol = mix( sgCol, sgDirt, sgDirtW * 0.85 );
	// 农田：地块、犁沟、田埂
	float sgFarm = 1.0 - sgMat.a;
	float sgFw = 0.0;
	if ( sgFarm > 0.02 ) {
		vec4 sgDB = texture2D( sgDetB, sgP * 0.11 + vec2( 0.13, 0.71 ) );
		vec3 sgCrop3 = sgCrop( sgDB.b ) * ( 0.8 + 0.34 * sgDB.a );
		sgCrop3 = mix( sgCrop3, vec3( 0.58, 0.5, 0.36 ), step( sgDB.b, 0.03 ) );
		sgCrop3 = mix( sgCrop3, sgTint, 0.15 );
		if ( sgNear > 0.01 ) {
			// 垄行：每块田一个方向（按田块编号），近处才画
			vec2 sgRD = vec2( cos( sgDB.b * 6.2832 ), sin( sgDB.b * 6.2832 ) );
			sgCrop3 *= mix( 1.0, 0.86 + 0.14 * sin( dot( sgP, sgRD ) * 38.0 ), sgNear * step( 0.03, sgDB.b ) );
		}
		sgFw = smoothstep( 0.3, 0.52, sgFarm + ( sgDM.r - 0.5 ) * 0.55 ) * ( 1.0 - smoothstep( 0.1, 0.26, sgSlope ) );
		sgCol = mix( sgCol, sgCrop3, sgFw );
	}
	// 沙：海滩与河岸（按高度）、沙漠（按群落）
	float sgBeach = ( 1.0 - smoothstep( 0.2, 0.48, sgH + ( sgDA.b - 0.5 ) * 0.14 ) ) * max( sgNearW, 1.0 - smoothstep( -0.02, 0.05, sgH ) );
	// 波纹只在沙漠里明显，海滩、河岸以细噪声为主
	float sgRip = mix( 0.08, 0.26, smoothstep( 0.3, 0.8, sgMat.r ) );
	vec3 sgSand = mix( vec3( 0.87, 0.8, 0.62 ), sgTint * 1.05, clamp( sgMat.r * 0.7, 0.0, 0.6 ) ) * ( 1.0 - sgRip * 0.55 + sgRip * sgDA.b + ( sgDF.r - 0.5 ) * 0.08 );
	sgSand *= mix( 0.8, 1.0, smoothstep( 0.0, 0.22, sgH ) );
	sgCol = mix( sgCol, sgSand, max( sgBeach, smoothstep( 0.2, 0.7, sgMat.r + ( sgDM.a - 0.5 ) * 0.25 ) * ( 1.0 - sgFw ) ) );
	// 岩石：陡坡与高山（三平面投影，避免峭壁上的纹理拉伸）
	float sgRockW = smoothstep( 0.4, 0.62, sgSlope + ( sgDM.a - 0.5 ) * 0.22 - sgMat.g * 0.12 );
	sgRockW = max( sgRockW, smoothstep( 4.4, 6.6, sgH + ( sgDM.r - 0.5 ) * 1.6 + sgMat.g * 1.0 ) );
	if ( sgRockW > 0.004 ) {
		vec3 sgBw = sgN * sgN; sgBw *= sgBw; sgBw /= ( sgBw.x + sgBw.y + sgBw.z );
		float sgR = texture2D( sgDetB, vSgW.zy * 0.19 ).r * sgBw.x + texture2D( sgDetB, vSgW.xz * 0.19 ).r * sgBw.y + texture2D( sgDetB, vSgW.xy * 0.19 ).r * sgBw.z;
		vec3 sgRockC = mix( vec3( 0.46, 0.44, 0.41 ), vec3( 0.56, 0.47, 0.37 ), clamp( sgMat.g, 0.0, 1.0 ) ) * ( 0.55 + 0.75 * sgR );
		sgCol = mix( sgCol, sgRockC, sgRockW );
		sgAO += ( sgR - 0.5 ) * 0.25 * sgRockW;
	}
	// 雪：雪线以上、坡度不太陡处，边缘带噪声
	float sgSnowW = smoothstep( 0.3, 0.62, sgMat.b + ( sgDA.a - 0.5 ) * 0.5 + ( sgDM.a - 0.5 ) * 0.3 - sgSlope * 0.7 );
	sgCol = mix( sgCol, vec3( 0.8, 0.83, 0.88 ) * ( 0.88 + 0.16 * sgDA.a ), sgSnowW );
	// 水下的海床
	sgCol = mix( sgCol, vec3( 0.6, 0.57, 0.45 ) * ( 0.85 + 0.2 * sgDA.b ), smoothstep( -0.05, -0.7, sgH ) * 0.75 );
	sgCol *= 0.74 + 0.52 * clamp( sgAO, 0.0, 1.0 );
	diffuseColor.rgb = sgCol;
`;
  const TERRAIN_FS_NORMAL = '#include <normal_fragment_maps>\n\tnormal = normalize( ( viewMatrix * vec4( sgN, 0.0 ) ).xyz );';
  // 高光肩部：与旧版地形材质相同（向阳的亮面不截断成纯白）
  const SHOULDER_KNEE = '0.8';
  const SHOULDER =
    '\tfloat sgPeak = max( max( outgoingLight.r, outgoingLight.g ), outgoingLight.b );\n' +
    '\tif ( sgPeak > ' + SHOULDER_KNEE + ' ) outgoingLight *= ( ' + SHOULDER_KNEE + ' + ( 1.0 - ' + SHOULDER_KNEE + ' ) * ( 1.0 - exp( ( ' + SHOULDER_KNEE +
    ' - sgPeak ) / ( 1.0 - ' + SHOULDER_KNEE + ' ) ) ) ) / sgPeak;\n' +
    '\t#include <opaque_fragment>';

  // 城池 / 关隘 / 桥的砖石材质（第三版修订）：在 LowPoly 光照之上按世界坐标程序化生成——
  // 竖直墙面为错缝砖石层（每块明暗略异、灰缝压暗；只作用于低饱和度的墙色，旗帜、门窗等彩色部分基本不变），
  // 坡面为筒瓦（沿檐方向的瓦垄 + 逐行的瓦缝），平顶为方砖。随镜头距离淡出（远处不出现摩尔纹）
  const MASONRY_VS_PARS = 'varying vec3 vSgMW;\nvarying vec3 vSgMN;\nvarying float vSgMD;\n';
  const MASONRY_VS_MAIN = '#include <project_vertex>\n\tvSgMW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n\tvSgMN = normalize( mat3( modelMatrix ) * objectNormal );\n\tvSgMD = - mvPosition.z;\n';
  const MASONRY_FS_PARS = `varying vec3 vSgMW;
varying vec3 vSgMN;
varying float vSgMD;
uniform vec2 sgMFade;
float sgMHash( vec2 p ) { return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) ) * 43758.5453 ); }
`;
  const MASONRY_FS = `#include <color_fragment>
	{
		float sgFd = 1.0 - smoothstep( sgMFade.x, sgMFade.y, vSgMD );
		if ( sgFd > 0.0 ) {
			vec3 n = normalize( vSgMN );
			vec3 p = vSgMW;
			vec3 c = diffuseColor.rgb;
			float mx = max( max( c.r, c.g ), c.b ), mn = min( min( c.r, c.g ), c.b );
			float sat = ( mx - mn ) / max( mx, 0.001 );
			float ay = abs( n.y );
			float k = 1.0;
			// 墙面：错缝砖石
			float wW = ( 1.0 - smoothstep( 0.25, 0.4, ay ) ) * clamp( 1.0 - ( sat - 0.25 ) * 2.2, 0.0, 1.0 );
			if ( wW > 0.0 ) {
				float u = abs( n.x ) > abs( n.z ) ? p.z : p.x;
				float row = floor( p.y / 0.052 );
				float uu = u / 0.12 + mod( row, 2.0 ) * 0.5;
				float fy = fract( p.y / 0.052 ), fu = fract( uu );
				float mort = 1.0 - smoothstep( 0.05, 0.16, min( fy, 1.0 - fy ) ) * smoothstep( 0.03, 0.08, min( fu, 1.0 - fu ) );
				float h = sgMHash( vec2( floor( uu ), row ) );
				k *= mix( 1.0, ( 0.9 + 0.16 * h ) * ( 1.0 - 0.24 * mort ), wW );
			}
			// 坡面：筒瓦
			float rW = smoothstep( 0.25, 0.4, ay ) * ( 1.0 - smoothstep( 0.9, 0.97, ay ) );
			if ( rW > 0.0 ) {
				vec2 t = normalize( vec2( - n.z, n.x ) + vec2( 1e-4, 0.0 ) );
				float u = dot( p.xz, t );
				float roll = 0.5 + 0.5 * sin( u / 0.04 * 6.2832 );
				float fy = fract( p.y / 0.03 );
				k *= mix( 1.0, ( 0.84 + 0.22 * roll * roll ) * ( 1.0 - 0.18 * ( 1.0 - smoothstep( 0.0, 0.22, fy ) ) ), rW );
			}
			// 平顶：方砖
			float fW = smoothstep( 0.9, 0.97, ay ) * clamp( 1.0 - ( sat - 0.25 ) * 2.2, 0.0, 1.0 );
			if ( fW > 0.0 ) {
				vec2 g = p.xz / 0.14;
				vec2 f = abs( fract( g ) - 0.5 );
				float j = 1.0 - smoothstep( 0.42, 0.48, max( f.x, f.y ) );
				k *= mix( 1.0, ( 0.93 + 0.1 * sgMHash( floor( g ) ) ) * ( 0.86 + 0.14 * j ), fW * 0.8 );
			}
			diffuseColor.rgb *= mix( 1.0, k, sgFd );
		}
	}
`;
  const _masonry = {};
  function masonryMaterial(near, far) {
    near = near || 16; far = far || 30;
    const key = near + ':' + far;
    if (_masonry[key]) return _masonry[key];
    const mat = new LowPolyMaterial();
    const u = { sgMFade: { value: new THREE.Vector2(near, far) } };
    mat.onBeforeCompile = function masonryCompile(shader, renderer) {
      lowPolyCompile.call(this, shader, renderer);
      Object.assign(shader.uniforms, u);
      shader.vertexShader = MASONRY_VS_PARS + shader.vertexShader.replace('#include <project_vertex>', MASONRY_VS_MAIN);
      shader.fragmentShader = MASONRY_FS_PARS + shader.fragmentShader.replace('#include <color_fragment>', MASONRY_FS);
    };
    mat.customProgramCacheKey = () => 'sgMasonry';
    mat.name = 'Masonry';
    _masonry[key] = mat;
    return mat;
  }

  Object.assign(Gfx, {
    // 砖石材质（城池、关隘、桥；共享实例，勿 dispose）：near / far 为淡出的镜头距离（缺省 16 / 30）
    masonryMaterial,
    // 地形细节纹理 { a, b }（同步生成，约 40ms；地图构建时用 terrainDetailGen 分片生成）
    terrainDetail() { if (!_detail) { const it = terrainDetailGen(); while (!it.next().done) { /* 同步跑完 */ } } return _detail; },
    terrainDetailGen,
    // 新建地形材质。opts.maps = { n, c, m, oc, om: DataTexture；xfN, xfC, xfO: Vector4（uv = 地图坐标 × zw + xy）；inner: Vector4(x0, z0, x1, z1)}
    terrainMaterial(opts) {
      const o = opts || {};
      const det = Gfx.terrainDetail();
      const mat = new LowPolyMaterial();
      const u = { sgDetA: { value: det.a }, sgDetB: { value: det.b } };
      if (o.maps) {
        const m = o.maps;
        mat.vertexColors = false;
        mat.defines = { SG_TERRAIN_MAPS: '' };
        Object.assign(u, {
          sgMapN: { value: m.n }, sgMapC: { value: m.c }, sgMapM: { value: m.m }, sgOutC: { value: m.oc }, sgOutM: { value: m.om },
          sgXfN: { value: m.xfN }, sgXfC: { value: m.xfC }, sgXfO: { value: m.xfO }, sgInner: { value: m.inner },
        });
      }
      mat.userData.terrainUniforms = u;
      mat.onBeforeCompile = function terrainCompile(shader, renderer) {
        lowPolyCompile.call(this, shader, renderer);
        Object.assign(shader.uniforms, u);
        shader.vertexShader = TERRAIN_VS_PARS + shader.vertexShader.replace('#include <project_vertex>', TERRAIN_VS_MAIN);
        shader.fragmentShader = TERRAIN_FS_PARS + shader.fragmentShader
          .replace('#include <color_fragment>', '#include <color_fragment>\n' + TERRAIN_FS_ALBEDO)
          .replace('#include <normal_fragment_maps>', TERRAIN_FS_NORMAL)
          .replace('#include <opaque_fragment>', SHOULDER);
      };
      mat.name = o.maps ? 'TerrainMaps' : 'TerrainVerts';
      return mat;
    },
  });

  SG.Gfx = Gfx;
  SG.MeshBuilder = MeshBuilder;
  SG.Models = Models;
  SG.LowPolyMaterial = LowPolyMaterial;
})();
