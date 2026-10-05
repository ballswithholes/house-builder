'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 渲染基础
   ← View/Art.cs（材质、程序化纹理、低多边形网格工具、常用模型）
   ← Resources/Shaders/*.shader（LowPoly 半兰伯特、Water、Sky、Unlit、Additive）
   ← Core/Game.cs SetupWorld()（太阳、三色环境光、雾、天空盒、阴影）

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
      }
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
      _basis.lookAt(SUN_DIR, _origin, _up);
      _basis.extractBasis(_bx, _by, _bz);
      const px = center.dot(_bx), py = center.dot(_by);
      const sx = Math.round(px / texel) * texel - px, sy = Math.round(py / texel) * texel - py;
      _v.copy(center).addScaledVector(_bx, sx).addScaledVector(_by, sy);
      const D = 220;
      sun.target.position.copy(_v);
      sun.position.copy(_v).addScaledVector(SUN_DIR, D);
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
      shared.equator.value.copy(this.equatorColor).multiplyScalar(this.hemi.intensity);
      this.sky.position.copy(this.camera.position);
      this.renderer.render(this.scene, this.camera);
      this._trackPerf();
    },
    _trackPerf() {
      const now = performance.now(), p = this._perf;
      if (this.autoQuality && p.last && now - this._t0 > 4000) {
        const dt = now - p.last;
        if (dt < 250) { p.acc += dt; p.n++; }
        if (p.n >= 120) {
          if (p.acc / p.n > 28) this.degrade();
          p.acc = 0; p.n = 0;
        }
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
    // 中式屋顶：四坡顶，檐角上翘
    chineseRoof(bc, w, d, h, col) {
      col = MeshBuilder._rgb(col);
      const ew = w * 0.5, ed = d * 0.5, cw = w * 0.32, cd = d * 0.3, ey = h * 0.1, cy = h * 0.36;
      const e0 = v3(bc.x - ew, bc.y + ey, bc.z - ed), e1 = v3(bc.x + ew, bc.y + ey, bc.z - ed), e2 = v3(bc.x + ew, bc.y + ey, bc.z + ed), e3 = v3(bc.x - ew, bc.y + ey, bc.z + ed);
      const c0 = v3(bc.x - cw, bc.y + cy, bc.z - cd), c1 = v3(bc.x + cw, bc.y + cy, bc.z - cd), c2 = v3(bc.x + cw, bc.y + cy, bc.z + cd), c3 = v3(bc.x - cw, bc.y + cy, bc.z + cd);
      const r0 = v3(bc.x - w * 0.22, bc.y + h, bc.z), r1 = v3(bc.x + w * 0.22, bc.y + h, bc.z);
      const dark = shade(col, -0.15);
      // 檐口（上翘的四角）
      this.quad(e0, c0, c1, e1, dark); this.quad(e1, c1, c2, e2, dark); this.quad(e2, c2, c3, e3, dark); this.quad(e3, c3, c0, e0, dark);
      // 屋面
      this.quad(c0, r0, r1, c1, col); this.quad(c2, r1, r0, c3, col);
      this.tri(c1, r1, c2, shade(col, -0.08)); this.tri(c3, r0, c0, shade(col, -0.08));
      // 正脊
      this.box(v3((r0.x + r1.x) * 0.5, (r0.y + r1.y) * 0.5 + 0.02, (r0.z + r1.z) * 0.5), v3(w * 0.55, h * 0.08, d * 0.06), shade(col, -0.4));
    }
    flag(pole, h, fw, fh, col) {
      col = MeshBuilder._rgb(col);
      this.box(v3(pole.x, pole.y + h * 0.5, pole.z), v3(0.04, h, 0.04), new THREE.Color(0.3, 0.22, 0.12));
      const tx = pole.x, ty = pole.y + h, tz = pole.z;
      const a = v3(tx + 0.02, ty, tz), b = v3(tx + fw, ty - fh * 0.1, tz);
      const c = v3(tx + fw, ty - fh * 1.1, tz), d = v3(tx + 0.02, ty - fh, tz);
      this.quad(a, b, c, d, col); this.quad(a, d, c, b, col);
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
  function nrm(x, y, z) { const l = Math.sqrt(x * x + y * y + z * z) || 1; return { x: x / l, y: y / l, z: z / l }; }
  // up, down, left, right, forward, back（Unity）
  const BLOB_PTS = [{ x: 0, y: 1, z: 0 }, { x: 0, y: -1, z: 0 }, { x: -1, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, { x: 0, y: 0, z: 1 }, { x: 0, y: 0, z: -1 }];
  const BLOB_FACES = [[0, 4, 3], [0, 3, 5], [0, 5, 2], [0, 2, 4], [1, 3, 4], [1, 5, 3], [1, 2, 5], [1, 4, 2]];

  // ------------------------------------------------------------- 常用模型 --
  const C = (r, g, b) => new THREE.Color(r, g, b);
  const addv = (p, x, y, z) => v3(p.x + x, p.y + y, p.z + z);
  const Models = {
    tree(mb, p, s, leaf, pine, seed) {
      mb.cylinder(p, 0.06 * s, 0.05 * s, 0.35 * s, 4, C(0.38, 0.27, 0.16), false);
      if (pine) {
        mb.cone(addv(p, 0, 0.25 * s, 0), 0.42 * s, 0.7 * s, 6, leaf);
        mb.cone(addv(p, 0, 0.6 * s, 0), 0.32 * s, 0.6 * s, 6, shade(leaf, 0.08));
      } else mb.blob(addv(p, 0, 0.65 * s, 0), v3(0.42 * s, 0.4 * s, 0.42 * s), leaf, seed);
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

  SG.Gfx = Gfx;
  SG.MeshBuilder = MeshBuilder;
  SG.Models = Models;
  SG.LowPolyMaterial = LowPolyMaterial;
})();
