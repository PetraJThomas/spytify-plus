import { useEffect, useState, type ReactNode } from 'react'
import { AnimatePresence, motion, useReducedMotion, useScroll, useSpring } from 'framer-motion'
import {
  Workflow,
  Combine,
  Microscope,
  Library,
  ShieldCheck,
  Sparkles,
  Github,
  Cpu,
  Check,
  Info,
  ZoomIn,
  X,
  Cable,
  ShieldAlert,
} from 'lucide-react'
import logoFull from './assets/logo-full.svg'
import logoMark from './assets/logo-mark.svg'
import recordShot from './assets/record.png'
import analyzeShot from './assets/analyze.png'
import templatesShot from './assets/templates.png'

const REPO = 'https://github.com/PetraJThomas/spytify-plus'
const ORIGINAL = 'https://github.com/jwallet/spy-spotify'
const FLAC_FORK = 'https://gitlab.com/Fora888/spytify-flac'
const PORTFOLIO = 'https://petrajthomas.com'

const EASE = [0.22, 1, 0.36, 1] as const

type Zoom = { src: string; alt: string }

/** Fade + rise into view once, respecting reduced-motion. */
function Reveal({
  children,
  delay = 0,
  className,
  id,
  as = 'div',
}: {
  children: ReactNode
  delay?: number
  className?: string
  id?: string
  as?: 'div' | 'section' | 'li'
}) {
  const reduce = useReducedMotion()
  const M = motion[as]
  return (
    <M
      id={id}
      className={className}
      initial={reduce ? false : { opacity: 0, y: 28 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.2 }}
      transition={{ duration: 0.6, ease: EASE, delay }}
    >
      {children}
    </M>
  )
}

/** A clickable screenshot that opens in the lightbox. */
function Screenshot({
  src,
  alt,
  caption,
  onZoom,
}: {
  src: string
  alt: string
  caption?: string
  onZoom: (z: Zoom) => void
}) {
  return (
    <figure className="shot">
      <div
        className="shot__frame"
        role="button"
        tabIndex={0}
        aria-label={`View ${alt}`}
        onClick={() => onZoom({ src, alt })}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            onZoom({ src, alt })
          }
        }}
      >
        <motion.img src={src} alt={alt} whileHover={{ y: -6 }} transition={{ duration: 0.35, ease: EASE }} />
        <span className="shot__zoom" aria-hidden>
          <ZoomIn size={18} />
        </span>
      </div>
      {caption && <figcaption>{caption}</figcaption>}
    </figure>
  )
}

const heroContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.12, delayChildren: 0.15 } },
}
const heroItem = {
  hidden: { opacity: 0, y: 24 },
  show: { opacity: 1, y: 0, transition: { duration: 0.7, ease: EASE } },
}

const HIGHLIGHTS = [
  {
    icon: Workflow,
    tag: 'Async Queue Engine',
    summary: 'Capture is decoupled from encoding, so fast track changes never drop a recording.',
    body: 'Recording and encoding were one inline, thread-blocking loop, so a slow encode or a fast track change silently dropped songs. I decoupled them into a single-consumer BlockingCollection<EncodeJob> pipeline: the recorder hands off the captured WAV and returns immediately, while a background worker encodes, moves, tags and cleans up off the capture path.',
  },
  {
    icon: Combine,
    tag: 'Unified FFmpeg Pipeline',
    summary: 'One stateless FFmpeg chain for every format; the native LAME binaries are gone.',
    body: 'Ripped out the legacy native LAME binaries (the libmp3lame DLLs plus the preset, resampler and validator) and routed every format — FLAC, OPUS, MP3, WAV — through one stateless FFmpeg encode/decode chain. Metadata travels via an ffmetadata file, so titles with quotes or backslashes can never corrupt the command.',
  },
  {
    icon: Microscope,
    tag: 'Native Forensics Engine',
    summary: "A built-in Spek-style analyzer that proves a file's real quality from its spectrum.",
    body: 'Decode any file with the bundled FFmpeg (no ffprobe), run a Hann-windowed FFT into an averaged magnitude spectrum, detect the brick-wall low-pass a lossy encoder leaves, and render a WriteableBitmap spectrogram (Inferno / Magma / Viridis / Heat) with a plain-language verdict. The codec is authoritative, so a 320 kbps source hidden in a FLAC wrapper is flagged as a transcode.',
  },
  {
    icon: Library,
    tag: 'Offline-Library Suite',
    summary: 'Templates, playlist-as-album, rich tags, cover.jpg and .m3u export, all opt-in.',
    body: 'Custom filename and folder templates with a click-to-insert tag builder, "record the current Spotify playlist as one album", recording-length verification, per-capture quality analysis, cover.jpg plus ISRC and Spotify track/album ID tags, and .m3u playlist export. All opt-in, and the captured audio is never resampled or transcoded.',
  },
  {
    icon: Sparkles,
    tag: 'Crafted UX & Motion',
    summary: 'A hand-tuned Fluent interface with a live album-art player card and fluid animations.',
    body: 'The WinForms UI was fully retired for a ModernWpf (Fluent) shell, dark with a Spotify-green accent: a mini Spotify-style player card showing the current track’s live album art, snap-proof navigation transitions, a tier-reactive Analyze verdict with a circulating glow-streak border, an equalizer-style busy loader, and consistent hover and press motion throughout. Fully localized in English and French.',
  },
  {
    icon: ShieldCheck,
    tag: 'Production Hardening',
    summary: '325/325 tests, English + French localization, and encrypted credentials at rest.',
    body: '325 of 325 unit tests pass (xUnit), behind a localization framework whose resx string tables are enforced against a key enum in English and French. Spotify API credentials are encrypted at rest with DPAPI.',
  },
]

function FeatureCard({ h, delay }: { h: (typeof HIGHLIGHTS)[number]; delay: number }) {
  const [open, setOpen] = useState(false)
  const Icon = h.icon
  return (
    <Reveal as="li" className="card" delay={delay}>
      <motion.div whileHover={{ y: -5 }} transition={{ duration: 0.3, ease: EASE }}>
        <div className="card__head">
          <span className="card__icon">
            <Icon size={20} strokeWidth={2} />
          </span>
          <span className="card__tag">{h.tag}</span>
          <button
            className="card__info"
            aria-label={open ? 'Hide details' : 'Show details'}
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
          >
            <Info size={16} />
          </button>
        </div>
        <p className="card__summary">{h.summary}</p>
        <AnimatePresence initial={false}>
          {open && (
            <motion.div
              className="card__more"
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.3, ease: EASE }}
            >
              <p className="card__body">{h.body}</p>
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>
    </Reveal>
  )
}

const LIBRARY = [
  'Templated paths: {albumartist}/{album} ({year}) and {track2} {title}, built by clicking tags.',
  'Turn a Spotify playlist into one cohesive "Various Artists" album, with its cover art.',
  'ISRC + Spotify track/album IDs written to tags, for de-duping and re-linking a library.',
  'cover.jpg per album folder and a portable .m3u playlist, alongside the embedded metadata.',
]

export default function App() {
  const { scrollYProgress } = useScroll()
  const progress = useSpring(scrollYProgress, { stiffness: 120, damping: 30, mass: 0.3 })
  const [zoom, setZoom] = useState<Zoom | null>(null)

  useEffect(() => {
    if (!zoom) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setZoom(null)
    window.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
    }
  }, [zoom])

  return (
    <div className="page">
      <motion.div className="scroll-progress" style={{ scaleX: progress }} />

      <header className="nav">
        <a className="nav__brand" href="#top">
          <img src={logoMark} alt="" width={26} height={26} />
          <span>Spytify+</span>
        </a>
        <nav className="nav__links">
          <a href="#engineering">Engineering</a>
          <a href="#quickstart">Quick start</a>
          <a className="nav__cta" href={REPO} target="_blank" rel="noreferrer">
            <Github size={16} /> GitHub
          </a>
        </nav>
      </header>

      <main id="top">
        {/* Phase 1 — Hero */}
        <section className="hero">
          <div className="hero__glow" aria-hidden />
          <motion.div className="hero__inner" variants={heroContainer} initial="hidden" animate="show">
            <motion.img className="hero__logo" variants={heroItem} src={logoFull} alt="Spytify+" />
            <motion.p className="hero__subtitle" variants={heroItem}>
              A modernized, decoupled, high-fidelity fork of the original Spytify project, optimized for
              bit-perfect lossless recording and native acoustic forensics.
            </motion.p>
            <motion.div className="hero__cta" variants={heroItem}>
              <a className="btn btn--primary" href={REPO} target="_blank" rel="noreferrer">
                <Github size={18} /> View on GitHub
              </a>
              <a className="btn btn--ghost" href="#engineering">
                <Cpu size={18} /> See the engineering
              </a>
            </motion.div>
          </motion.div>

          <motion.div
            className="shots"
            variants={heroItem}
            initial="hidden"
            animate="show"
            transition={{ delay: 0.5 }}
          >
            <Screenshot
              src={recordShot}
              alt="Spytify+ Record tab: the player card with live album art and the async processing log"
              caption="Record: live album-art player card + async capture log"
              onZoom={setZoom}
            />
            <Screenshot
              src={analyzeShot}
              alt="Spytify+ Analyze tab: waveform, frequency response and Inferno spectrogram with a lossless verdict"
              caption="Analyze: waveform, spectrogram and a plain-language verdict"
              onZoom={setZoom}
            />
          </motion.div>
        </section>

        {/* Phase 2 — Origins & Credits */}
        <Reveal as="section" className="section origins">
          <h2 className="section__title">Origins &amp; Credits</h2>
          <p>
            Spytify+ is built on the foundational work of{' '}
            <a href={ORIGINAL} target="_blank" rel="noreferrer">
              Spytify by jwallet
            </a>
            . Native FLAC output came from the intermediate{' '}
            <a href={FLAC_FORK} target="_blank" rel="noreferrer">
              spytify-flac fork by Fora888
            </a>
            . This project is the WPF rewrite, the forensics engine and the offline-library work on top.
          </p>
          <p>
            Every layer stays{' '}
            <strong>100% open-source under the original MIT License</strong>, preserving the full
            historical and legal chain back to jwallet&apos;s project. No donation links, badges or
            monetization live here: it is kept strictly altruistic.
          </p>
        </Reveal>

        {/* Phase 3 — Engineering highlights */}
        <section id="engineering" className="section">
          <Reveal>
            <h2 className="section__title">Engineering highlights</h2>
            <p className="section__lead">
              The phased technical work behind the fork, not just &ldquo;it&apos;s better.&rdquo;
            </p>
          </Reveal>
          <ul className="cards">
            {HIGHLIGHTS.map((h, i) => (
              <FeatureCard h={h} delay={i * 0.06} key={h.tag} />
            ))}
          </ul>
        </section>

        {/* Library / organisation, with the template builder shot */}
        <section className="section split">
          <Reveal className="split__text">
            <h2 className="section__title">Built for a real library</h2>
            <ul className="ticks">
              {LIBRARY.map((l) => (
                <li key={l}>
                  <Check className="tick__icon" size={18} strokeWidth={2.5} />
                  <span>{l}</span>
                </li>
              ))}
            </ul>
          </Reveal>
          <Reveal className="split__media" delay={0.1}>
            <Screenshot
              src={templatesShot}
              alt="Spytify+ filename and folder template builder with a click-to-insert tag legend"
              onZoom={setZoom}
            />
          </Reveal>
        </section>

        {/* Phase 4 — Quick start */}
        <Reveal as="section" id="quickstart" className="section">
          <h2 className="section__title">Quick start</h2>
          <ol className="steps">
            <li>
              <span className="steps__icon">
                <Cable size={20} />
              </span>
              <div>
                <h3>Record from the loopback</h3>
                <p>
                  Install a virtual audio cable (e.g. VB-Audio), set Windows playback and Spytify+ to it
                  at <strong>44.1 kHz</strong>, and record. You capture exactly what Spotify plays out,
                  with nothing else mixed in.
                </p>
              </div>
            </li>
            <li>
              <span className="steps__icon">
                <ShieldAlert size={20} />
              </span>
              <div>
                <h3>Get past Windows SmartScreen</h3>
                <p>
                  The release is unsigned, so SmartScreen shows an &ldquo;unknown publisher&rdquo; prompt
                  the first time. Click <strong>More info &rarr; Run anyway</strong>, and unblock the
                  downloaded <code>.zip</code> (right-click &rarr; Properties &rarr; Unblock) before
                  extracting. Or build it yourself from source.
                </p>
              </div>
            </li>
          </ol>
        </Reveal>
      </main>

      <footer className="footer">
        <div className="footer__credit">
          Maintained and re-engineered by{' '}
          <a href={PORTFOLIO} target="_blank" rel="noreferrer">
            Petra J. Thomas
          </a>{' '}
          &middot; Founder of TR Studio Pro
        </div>
        <div className="footer__links">
          <a href={REPO} target="_blank" rel="noreferrer">
            GitHub
          </a>
          <a href={ORIGINAL} target="_blank" rel="noreferrer">
            Original project
          </a>
          <a href={`${REPO}/blob/main/LICENSE`} target="_blank" rel="noreferrer">
            MIT License
          </a>
        </div>
        <div className="footer__fine">MIT licensed. Not affiliated with Spotify.</div>
      </footer>

      {/* Lightbox */}
      <AnimatePresence>
        {zoom && (
          <motion.div
            className="lightbox"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={() => setZoom(null)}
          >
            <button className="lightbox__close" aria-label="Close" onClick={() => setZoom(null)}>
              <X size={22} />
            </button>
            <motion.img
              src={zoom.src}
              alt={zoom.alt}
              initial={{ scale: 0.94, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.96, opacity: 0 }}
              transition={{ duration: 0.25, ease: EASE }}
              onClick={(e) => e.stopPropagation()}
            />
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
