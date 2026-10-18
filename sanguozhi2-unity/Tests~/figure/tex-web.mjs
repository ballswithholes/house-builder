// 人物模型库的贴图参考（网页版，headless Chromium 的真 Canvas2D）：
//   程序图集（map / normal / ORM，DataTexture 的原始 RGBA，第 0 行 = v 0）、若干名将面部贴图（画布 RGBA，第 0 行 = 顶边）、环境贴图（256×128）。
//   以及环境贴图经 three PMREM 预滤波后按方向 × 粗糙度的取样（WebGL，= textureCubeUV）。
//   输出：<目录>/web-atlas-map.rgba、web-atlas-nrm.rgba、web-atlas-orm.rgba、web-env.rgba、web-face-<i>.rgba、web-tex.json（清单 + 武将数据）、web-pmrem.json
// 用法：node tex-web.mjs <输出目录>      （由 Tests~/figure/tex.sh 调用；C# 端比对见 FigureTex.cs）
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(here, '../../../sanguozhi2-web');
const outDir = path.resolve(process.argv[2] || path.join(here, '../out/figure/tex'));
fs.mkdirSync(outDir, { recursive: true });
// 面部贴图：[武将, 尺寸]（512 = 高精度，256 = 中，128 = 低）
const FACES = [['关羽', 512], ['张飞', 512], ['赵云', 512], ['吕布', 512], ['诸葛亮', 512], ['马超', 512], ['夏侯惇', 512], ['黄忠', 512],
  ['卑弥呼', 512], ['塞维鲁', 512], ['沃洛吉斯', 512], ['加达拉特', 512], ['鲁德拉布提', 512], ['龙树', 512], ['阿根托科克斯', 512], ['貂蝉', 512],
  ['曹操', 256], ['孙权', 128]];

const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const page = await browser.newPage();
page.on('pageerror', e => console.log('页面错误', e.message));
await page.setContent('<!doctype html><html><head><meta charset="utf-8"></head><body></body></html>');
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/data.js', 'js/world-data.js', 'js/portrait-data.js', 'js/portrait.js'])
  await page.addScriptTag({ content: fs.readFileSync(path.join(WEB, f), 'utf8') });
// 闭包里的 faceTexture / envMap 通过在 SG.Figure._internal 处注入一个只读出口取得（源码不改动）
const hook = '_internal: {';
let fig = fs.readFileSync(path.join(WEB, 'js/figure.js'), 'utf8');
if (!fig.includes(hook)) throw new Error('figure.js 里找不到 SG.Figure._internal');
fig = fig.replace(hook, '_internal: { __tex: { faceTexture, envMap }, ');
await page.addScriptTag({ content: fig });

const res = await page.evaluate((FACES) => {
  const b64 = u8 => { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000)); return btoa(s); };
  // 武将数据：与 tests/figures.html 相同
  const W = SG.Scenarios.world, GEN = {}, facColor = {};
  for (const l of W.Factions) { const p = l.split('|'); facColor[p[0]] = p[3]; }
  for (const l of W.Generals) { const p = l.split('|'); GEN[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], culture: p[9] || 'han', color: facColor[p[4]] || '#8a8a8a' }; }
  const gen = n => GEN[n] || { name: n, war: 80, intel: 60, pol: 50 };
  const t0 = performance.now();
  const A = SG.Figure.atlas();
  const atlasMs = performance.now() - t0;
  const I = SG.Figure._internal.__tex;
  const out = { atlasMs, map: b64(A.map.image.data), nrm: b64(A.normalMap.image.data), orm: b64(A.ormMap.image.data), faces: [] };
  for (const [name, size] of FACES) {
    const g = gen(name), lod = size === 512 ? 'high' : size === 256 ? 'mid' : 'low';
    const look = SG.Figure.lookOf(g, { lod });
    const t = I.faceTexture(look.spec || {}, size);
    const cv = t.image;
    out.faces.push({ name, size, lod, gen: g, data: b64(cv.getContext('2d').getImageData(0, 0, size, size).data) });
  }
  const env = I.envMap().image;
  out.env = b64(env.getContext('2d').getImageData(0, 0, env.width, env.height).data);
  out.envW = env.width; out.envH = env.height;
  return out;
}, FACES);

// 环境贴图经 three 的 PMREM 预滤波后的取样（= MeshStandardMaterial 的 textureCubeUV）：Fibonacci 球面上 256 个方向（three 坐标）×
// 16 个粗糙度（最后一个 1.0 = getIBLIrradiance 的取样），WebGL 渲染到 8 位目标后读回 → web-pmrem.json（C# 的 _EnvCube / _EnvIrr 对照）
// 粗糙度：PMREM 各整数 mip 处（6 … −2：0.1078 0.1524 0.21 0.305 0.4 0.5333 0.6667 0.8 1.0）与其间的若干值
const PM_R = [0.0525, 0.1078, 0.1524, 0.21, 0.25, 0.305, 0.35, 0.4, 0.47, 0.5333, 0.6, 0.6667, 0.73, 0.8, 0.9, 1.0], PM_N = 256;
const pmrem = await page.evaluate(([N, R]) => {
  THREE.ColorManagement.enabled = false;            // 与游戏相同（art.js）：颜色原样
  const cv = document.createElement('canvas'); cv.width = N; cv.height = R.length;
  const rr = new THREE.WebGLRenderer({ canvas: cv });
  rr.outputColorSpace = THREE.LinearSRGBColorSpace;
  const env = SG.Figure._internal.__tex.envMap();
  const gen = new THREE.PMREMGenerator(rr);
  const pm = gen.fromEquirectangular(env);
  const dirs = new Float32Array(N * 4), ga = Math.PI * (3 - Math.sqrt(5));
  for (let i = 0; i < N; i++) { const y = 1 - (i + 0.5) / N * 2, r = Math.sqrt(1 - y * y), th = ga * i; dirs.set([Math.cos(th) * r, y, Math.sin(th) * r, 1], i * 4); }
  const dt = new THREE.DataTexture(dirs, N, 1, THREE.RGBAFormat, THREE.FloatType); dt.needsUpdate = true;
  const H = pm.texture.image.height, maxMip = Math.log2(H) - 2;
  const mat = new THREE.ShaderMaterial({
    defines: { ENVMAP_TYPE_CUBE_UV: '', CUBEUV_TEXEL_WIDTH: 1.0 / (3 * Math.max(Math.pow(2, maxMip), 7 * 16)), CUBEUV_TEXEL_HEIGHT: 1.0 / H, CUBEUV_MAX_MIP: maxMip.toFixed(1), NR: R.length, ND: N.toFixed(1) },
    uniforms: { envMap: { value: pm.texture }, dirTex: { value: dt }, rough: { value: R } },
    vertexShader: 'void main() { gl_Position = vec4( position.xy * 2.0, 0.0, 1.0 ); }',
    fragmentShader: ['uniform sampler2D envMap; uniform sampler2D dirTex; uniform float rough[ NR ];', '#include <common>', '#include <cube_uv_reflection_fragment>',
      'void main() {', '  vec3 d = texture2D( dirTex, vec2( ( floor( gl_FragCoord.x ) + 0.5 ) / ND, 0.5 ) ).xyz;', '  int iy = int( gl_FragCoord.y ); float r = 1.0;',
      '  for ( int k = 0; k < NR; k ++ ) if ( k == iy ) r = rough[ k ];', '  gl_FragColor = vec4( textureCubeUV( envMap, d, r ).rgb, 1.0 );', '}'].join('\n'),
  });
  const scene = new THREE.Scene(); scene.add(new THREE.Mesh(new THREE.PlaneGeometry(1, 1), mat));
  const rt = new THREE.WebGLRenderTarget(N, R.length);
  rr.setRenderTarget(rt); rr.render(scene, new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1)); rr.setRenderTarget(null);
  const px = new Uint8Array(N * R.length * 4); rr.readRenderTargetPixels(rt, 0, 0, N, R.length, px);
  return { n: N, rough: R, dirs: Array.from(dirs), px: Array.from(px), size: pm.texture.image.width + 'x' + H };
}, [PM_N, PM_R]);
fs.writeFileSync(path.join(outDir, 'web-pmrem.json'), JSON.stringify(pmrem));

const put = (f, s) => fs.writeFileSync(path.join(outDir, f), Buffer.from(s, 'base64'));
put('web-atlas-map.rgba', res.map); put('web-atlas-nrm.rgba', res.nrm); put('web-atlas-orm.rgba', res.orm); put('web-env.rgba', res.env);
const list = res.faces.map((f, i) => { put('web-face-' + i + '.rgba', f.data); return { name: f.name, size: f.size, lod: f.lod, gen: f.gen, file: 'web-face-' + i + '.rgba' }; });
fs.writeFileSync(path.join(outDir, 'web-tex.json'), JSON.stringify({ atlasMs: res.atlasMs, envW: res.envW, envH: res.envH, faces: list }, null, 1));
console.log('网页版贴图：图集 ' + Math.round(res.atlasMs) + ' ms，面部 ' + list.length + ' 张，环境 ' + res.envW + '×' + res.envH + ' → ' + outDir);
await browser.close();
