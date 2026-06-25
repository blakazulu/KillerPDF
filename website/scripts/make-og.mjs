// Generates the social-share image + raster app icons from the brand SVG.
//
//   node scripts/make-og.mjs
//
// Renders branded HTML to PNG with the already-installed Playwright Chromium
// (a website devDependency). Outputs land in public/ and are committed, so the
// generator only needs to run when the brand or copy changes — Netlify never
// runs it. SVG og:images don't render on WhatsApp/LinkedIn/iMessage/Twitter, so
// these rasters are what makes the site shareable.
import { chromium } from 'playwright'
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const __dirname = dirname(fileURLToPath(import.meta.url))
const pub = resolve(__dirname, '..', 'public')
mkdirSync(pub, { recursive: true })

// The brand mark (steel squircle + tilted doc + scalpel with a red edge).
const iconSvg = readFileSync(resolve(pub, 'scalpel-icon.svg'), 'utf8')

// ---- Brand tokens (mirrors src/styles/tokens.css "Clinical" identity) ----
const STEEL = '#2C3A4C'
const STEEL2 = '#1A2430'
const RED = '#E11D38'
const PAPER = '#F4F6F8'

const fontStack = `'Segoe UI','Helvetica Neue',Arial,system-ui,sans-serif`

// ---------- The 1200x630 social card (Open Graph / Twitter / LinkedIn) ----------
const ogHtml = `<!doctype html><html><head><meta charset="utf-8"><style>
  *{margin:0;padding:0;box-sizing:border-box}
  html,body{width:1200px;height:630px}
  body{
    font-family:${fontStack};
    background:
      radial-gradient(120% 140% at 100% 0%, rgba(225,29,56,0.18), transparent 55%),
      linear-gradient(150deg, ${STEEL} 0%, ${STEEL2} 100%);
    color:#fff; overflow:hidden; position:relative;
  }
  /* faint clinical grid */
  body::before{content:"";position:absolute;inset:0;
    background-image:linear-gradient(rgba(255,255,255,.05) 1px,transparent 1px),
      linear-gradient(90deg,rgba(255,255,255,.05) 1px,transparent 1px);
    background-size:48px 48px;mask-image:linear-gradient(120deg,#000,transparent 80%)}
  .edge{position:absolute;inset:0 auto 0 0;width:12px;background:${RED}}
  .wrap{position:relative;height:100%;display:flex;align-items:center;
    gap:56px;padding:72px 84px 72px 96px}
  .left{flex:1 1 0;min-width:0}
  .brandrow{display:flex;align-items:center;gap:18px;margin-bottom:36px}
  .brandrow svg{width:64px;height:64px;border-radius:16px;
    box-shadow:0 10px 30px rgba(0,0,0,.45)}
  .word{font-size:40px;font-weight:800;letter-spacing:-.02em}
  .word b{color:#fff}.word i{color:${RED};font-style:normal}
  h1{font-size:58px;line-height:1.05;font-weight:800;letter-spacing:-.035em;margin-bottom:22px;max-width:14ch}
  h1 .red{color:#FF6173}
  .lead{font-size:26px;line-height:1.4;color:#C7D0DC;max-width:30ch;font-weight:450}
  .pills{display:flex;flex-wrap:wrap;gap:12px;margin-top:34px}
  .pill{font-size:19px;font-weight:600;padding:8px 15px;border-radius:999px;
    background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.16);color:#E7ECF2}
  .foot{position:absolute;left:96px;bottom:46px;display:flex;align-items:center;gap:14px;
    font-size:22px;color:#9FB0C2;font-weight:600}
  .foot .dot{width:5px;height:5px;border-radius:50%;background:#5C6E82}
  .foot .free{color:#fff;background:${RED};padding:6px 16px;border-radius:999px;font-weight:800}
  /* app-window mock on the right */
  .right{flex:0 0 360px;align-self:center}
  .frame{background:#0E1116;border:1px solid rgba(255,255,255,.12);border-radius:20px;
    box-shadow:0 40px 80px -24px rgba(0,0,0,.7);overflow:hidden}
  .bar{height:40px;background:${STEEL2};display:flex;align-items:center;gap:8px;padding:0 16px;
    border-bottom:1px solid rgba(255,255,255,.08)}
  .bar i{width:11px;height:11px;border-radius:50%;display:inline-block}
  .bar .n{margin-left:14px;font-size:14px;color:#8FA0B3}
  .stage{height:300px;display:flex;align-items:center;justify-content:center;
    background:radial-gradient(80% 80% at 50% 40%, #1b2532, #0E1116)}
  .stage svg{width:184px;height:184px;filter:drop-shadow(0 18px 36px rgba(0,0,0,.5))}
</style></head><body>
  <div class="edge"></div>
  <div class="wrap">
    <div class="left">
      <div class="brandrow">${iconSvg}<div class="word"><b>Scalpel</b><i>PDF</i></div></div>
      <h1>Precise PDF editing. <span class="red">Nothing leaves your machine.</span></h1>
      <div class="pills">
        <span class="pill">View</span><span class="pill">Annotate</span>
        <span class="pill">Edit</span><span class="pill">Sign</span>
        <span class="pill">OCR</span><span class="pill">Redact</span>
      </div>
    </div>
    <div class="right">
      <div class="frame">
        <div class="bar"><i style="background:#FF5F57"></i><i style="background:#FEBC2E"></i><i style="background:#28C840"></i><span class="n">Acquisition.pdf — Scalpel</span></div>
        <div class="stage">${iconSvg}</div>
      </div>
    </div>
  </div>
  <div class="foot">
    <span class="free">Free</span><span>Local-only Windows PDF editor</span>
    <span class="dot"></span><span>~6&nbsp;MB EXE</span>
    <span class="dot"></span><span>No telemetry</span>
    <span class="dot"></span><span>GPLv3</span>
  </div>
</body></html>`

// ---------- A plain raster of the brand mark for icons ----------
function iconHtml(px, pad = 0) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>
    *{margin:0;padding:0}html,body{width:${px}px;height:${px}px;background:transparent}
    body{display:flex;align-items:center;justify-content:center}
    svg{width:${px - pad * 2}px;height:${px - pad * 2}px}
  </style></head><body>${iconSvg}</body></html>`
}

const browser = await chromium.launch()

async function shoot(html, w, h, out, transparent = false) {
  const page = await browser.newPage({
    viewport: { width: w, height: h },
    deviceScaleFactor: 1,
  })
  await page.setContent(html, { waitUntil: 'networkidle' })
  const buf = await page.screenshot({ omitBackground: transparent })
  writeFileSync(resolve(pub, out), buf)
  await page.close()
  console.log('  ✓', out, `(${w}x${h})`)
}

console.log('Rendering social + icon assets →', pub)
await shoot(ogHtml, 1200, 630, 'og-image.png')
await shoot(iconHtml(512), 512, 512, 'icon-512.png', true)
await shoot(iconHtml(192), 192, 192, 'icon-192.png', true)
await shoot(iconHtml(180), 180, 180, 'apple-touch-icon.png', true)

await browser.close()
console.log('Done.')
