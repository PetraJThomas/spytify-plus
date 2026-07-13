import { useEffect, useState, type ReactNode } from 'react'
import {
  AnimatePresence,
  motion,
  useMotionValueEvent,
  useReducedMotion,
  useScroll,
  useSpring,
} from 'framer-motion'
import {
  Workflow,
  Combine,
  Microscope,
  Library,
  ListChecks,
  ShieldCheck,
  Sparkles,
  Github,
  Cpu,
  Download,
  Check,
  Info,
  ZoomIn,
  X,
  Cable,
  BadgeCheck,
  SlidersHorizontal,
  ArrowRightLeft,
  CircleDot,
  ArrowUp,
} from 'lucide-react'
import logoFull from './assets/logo-full.svg'
import logoMark from './assets/logo-mark.svg'
import recordShot from './assets/record.webp'
import analyzeShot from './assets/analyze.webp'
import templatesShot from './assets/templates.webp'

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
  w,
  h,
}: {
  src: string
  alt: string
  caption?: string
  onZoom: (z: Zoom) => void
  w: number
  h: number
}) {
  return (
    <figure className="shot">
      <div
        className="shot__frame"
        role="button"
        tabIndex={0}
        aria-label={`View a larger image: ${alt}`}
        onClick={() => onZoom({ src, alt })}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            onZoom({ src, alt })
          }
        }}
      >
        <motion.img
          src={src}
          alt={alt}
          width={w}
          height={h}
          whileHover={{ y: -6 }}
          transition={{ duration: 0.35, ease: EASE }}
        />
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

const STATS = ['349 tests, all green', 'FLAC · OPUS · MP3 · WAV', 'English + French', '.NET Framework 4.8', 'MIT licensed']

const HIGHLIGHTS = [
  {
    icon: Workflow,
    tag: 'Perfect file recordings, every time',
    summary: 'Recording and encoding run on separate lanes now, so a quick skip never loses a track.',
    body: 'The recorder and the encoder used to be tangled in one blocking loop, so a slow encode or a fast track change would quietly lose a song. I split them apart: the recorder grabs the audio, hands it off, and moves straight on, while a background worker encodes, files and tags it out of the way.',
  },
  {
    icon: Combine,
    tag: 'One path for every format',
    summary: 'FLAC, OPUS, MP3 and WAV all go through a single FFmpeg step. The old native encoders are gone.',
    body: 'I pulled out the legacy native LAME encoder and its pile of helpers, and now every format runs through one FFmpeg chain. Tags travel in their own file, so a song title full of quotes or backslashes can never break the command.',
  },
  {
    icon: Microscope,
    tag: 'It can prove the quality',
    summary: "A built-in analyzer reads a file's spectrum and tells you, in plain English, what you really got.",
    body: 'Point it at any audio file and it decodes with the bundled FFmpeg, runs the maths over the frequency spectrum, and spots the tell-tale wall a lossy encoder leaves behind. You get a spectrogram and a verdict, so a 320 kbps file dressed up as a FLAC gets caught.',
  },
  {
    icon: Library,
    tag: 'Built for a tidy library',
    summary: 'Name your files with templates, turn a playlist into an album, and get real tags, cover art and playlists.',
    body: 'Name and file your recordings however you like with a click-to-build template, turn a whole Spotify playlist into one album, write ISRC and Spotify IDs into the tags, drop a cover.jpg in each folder, and export an .m3u. It is all optional, and none of it touches the audio itself.',
  },
  {
    icon: ListChecks,
    tag: 'Keep an existing library honest',
    summary: 'The Check Library tab sweeps a whole folder of recordings: grade every file’s real quality, and refresh tags and cover art from Spotify.',
    body: 'It works over music you already have, not just new captures. Analyse Library grades each file’s true quality in one batch and flags anything that is not what it claims. Update Library Metadata refreshes tags and cover art straight from Spotify, matched exactly by each file’s embedded ISRC so it can never mistag, with no re-recording involved.',
  },
  {
    icon: Sparkles,
    tag: 'It actually feels good to use',
    summary: 'A hand-built interface, dark and Spotify-green, with a live now-playing card and motion where it helps.',
    body: 'I threw out the old WinForms interface and rebuilt it in WPF. There is a little now-playing card with the live album art, navigation that never stutters, a glowing verdict badge on the Analyze tab, an equalizer-style loader, and animation everywhere it earns its place. It is fully translated into English and French too.',
  },
  {
    icon: ShieldCheck,
    tag: 'Solid, not just shiny',
    summary: '349 tests passing, two languages kept in sync automatically, and your API keys encrypted on disk.',
    body: 'There are 349 tests and they all pass. Every piece of on-screen text is translated across English and French, checked automatically so nothing slips through untranslated. And your Spotify API keys are encrypted where they are stored, not sitting in plain text.',
  },
]

const LIBRARY = [
  'Build paths by clicking tags: {albumartist}/{album} ({year}), then {track2} {title}.',
  'Turn a whole Spotify playlist into one album, cover art and all.',
  'ISRC and Spotify IDs written into the tags, so nothing is a mystery later.',
  'A cover.jpg in every folder and an .m3u playlist to match.',
]

const SETUP = [
  {
    icon: BadgeCheck,
    title: 'Turn on Lossless in Spotify',
    body: "It's included with every Premium plan, just switched off by default. In Spotify, open Settings, find Audio quality, and set it to Lossless.",
  },
  {
    icon: Cable,
    title: 'Add the virtual audio cable',
    body: 'Spytify+ can install the VB-Audio cable for you from its Settings, or you can grab it yourself. It gives Spotify a private lane that nothing else touches.',
  },
  {
    icon: SlidersHorizontal,
    title: 'Match the format: 44.1 kHz, 24-bit',
    body: "In Windows Sound settings, open the CABLE Input device and set its format to 44.1 kHz, 24-bit. Matching Spotify's own output is the whole trick: nothing gets resampled, so the capture stays bit-perfect.",
  },
  {
    icon: ArrowRightLeft,
    title: "Send Spotify's audio to the cable",
    body: 'In Windows, point Spotify at CABLE Input (Sound settings, or its per-app output in the volume mixer). Now everything it plays goes down the cable.',
  },
  {
    icon: CircleDot,
    title: 'Point Spytify+ at it and record',
    body: 'In Spytify+ Settings, pick CABLE Input as the device, hit record, and away you go. The Analyze tab will confirm you got a true, full-band lossless capture.',
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
            aria-label={open ? 'Hide details' : 'Show the detail'}
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

export default function App() {
  const { scrollYProgress } = useScroll()
  const progress = useSpring(scrollYProgress, { stiffness: 120, damping: 30, mass: 0.3 })
  const [zoom, setZoom] = useState<Zoom | null>(null)
  const [showTop, setShowTop] = useState(false)
  const reduce = useReducedMotion()
  useMotionValueEvent(scrollYProgress, 'change', (v) => setShowTop(v > 0.12))

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
          <a href="#build">The build</a>
          <a href="#setup">Setup</a>
          <a className="nav__cta" href={REPO} target="_blank" rel="noreferrer">
            <Github size={16} /> GitHub
          </a>
        </nav>
      </header>

      <main id="top">
        {/* Hero */}
        <section className="hero">
          <div className="hero__glow" aria-hidden />
          <motion.div
            className="hero__inner"
            variants={heroContainer}
            initial={reduce ? false : 'hidden'}
            animate="show"
          >
            <motion.h1 className="hero__logo" variants={heroItem}>
              <img src={logoFull} alt="Spytify+" width={1460} height={429} />
            </motion.h1>
            <motion.p className="hero__subtitle" variants={heroItem}>
              A from-scratch rebuild of Spytify, the Spotify recorder. It captures your music cleanly,
              keeps every track tagged and filed, and shows you exactly how lossless the result really is.
            </motion.p>
            <motion.div className="hero__cta" variants={heroItem}>
              <a className="btn btn--primary" href={REPO} target="_blank" rel="noreferrer">
                <Github size={18} /> View on GitHub
              </a>
              <a className="btn btn--ghost" href="#build">
                <Cpu size={18} /> See how it's built
              </a>
            </motion.div>
          </motion.div>

          <motion.div
            className="shots"
            variants={heroItem}
            initial={reduce ? false : 'hidden'}
            animate="show"
            transition={{ delay: 0.5 }}
          >
            <Screenshot
              src={recordShot}
              w={1349}
              h={737}
              alt="Spytify+ Record tab: the player card with live album art and the capture log"
              caption="Recording, with a live now-playing card and the capture log"
              onZoom={setZoom}
            />
            <Screenshot
              src={analyzeShot}
              w={1349}
              h={1392}
              alt="Spytify+ Analyze tab: waveform, frequency response and Inferno spectrogram with a lossless verdict"
              caption="Analyzing a file: waveform, spectrogram and a plain-English verdict"
              onZoom={setZoom}
            />
          </motion.div>
        </section>

        {/* Stats strip */}
        <Reveal className="stats">
          {STATS.map((s) => (
            <span className="stat" key={s}>
              {s}
            </span>
          ))}
        </Reveal>

        {/* Why it exists */}
        <Reveal as="section" className="section why">
          <h2 className="section__title">The lossless illusion</h2>
          <p>
            Record Spotify through a loopback, get a <code>.flac</code>, and it looks lossless. Usually it
            isn't. What Spotify plays out is capped: 320 kbps most of the time, and genuinely bit-perfect
            only if you have Spotify Lossless and a cable set to 44.1 kHz. Spytify+ captures whatever comes
            out, faithfully, and then hands you the tools to see exactly what that was.
          </p>
        </Reveal>

        {/* Origins & Credits */}
        <Reveal as="section" className="section origins">
          <h2 className="section__title">Origins &amp; credits</h2>
          <p>
            Spytify+ stands on{' '}
            <a href={ORIGINAL} target="_blank" rel="noreferrer">
              jwallet's original Spytify
            </a>
            , with FLAC support picked up from{' '}
            <a href={FLAC_FORK} target="_blank" rel="noreferrer">
              Fora888's fork
            </a>{' '}
            along the way. What I added on top is the WPF rewrite, the analysis engine, and everything
            around building a proper offline library.
          </p>
          <p>
            It all stays open-source under the same <strong>MIT license</strong>, straight back to the
            original. No donation buttons, badges or trackers live here: it is kept purely for the love of
            the thing. Genuine thanks to jwallet and Fora888 for the groundwork.
          </p>
        </Reveal>

        {/* The build (engineering highlights) */}
        <section id="build" className="section">
          <Reveal>
            <h2 className="section__title">Under the hood</h2>
            <p className="section__lead">The real work behind the rebuild, not just &ldquo;trust me, it's better.&rdquo;</p>
          </Reveal>
          <ul className="cards">
            {HIGHLIGHTS.map((h, i) => (
              <FeatureCard h={h} delay={i * 0.06} key={h.tag} />
            ))}
          </ul>
        </section>

        {/* Library, with the template builder shot */}
        <section className="section split">
          <Reveal className="split__text">
            <h2 className="section__title">A library, not a folder full of files</h2>
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
              w={1349}
              h={737}
              alt="Spytify+ filename and folder template builder with a click-to-insert tag legend"
              onZoom={setZoom}
            />
          </Reveal>
        </section>

        {/* Bit-perfect setup */}
        <Reveal as="section" id="setup" className="section">
          <h2 className="section__title">Getting bit-perfect</h2>
          <p className="section__lead">
            Spotify Lossless is included with every Premium plan, it just needs enabling. Here's the
            whole chain, end to end.
          </p>
          <ol className="steps">
            {SETUP.map((s) => {
              const Icon = s.icon
              return (
                <li key={s.title}>
                  <span className="steps__icon">
                    <Icon size={20} />
                  </span>
                  <div>
                    <h3>{s.title}</h3>
                    <p>{s.body}</p>
                  </div>
                </li>
              )
            })}
          </ol>
          <p className="requirements">
            Runs on Windows with the Spotify desktop app and .NET Framework 4.8, already on most
            machines.
          </p>
          <div className="notice" role="note">
            <ShieldCheck className="notice__icon" size={24} aria-hidden />
            <div className="notice__body">
              <strong>Heads up: Windows will warn you on first launch.</strong>
              <p>
                Spytify+ is open-source and unsigned, so Windows SmartScreen shows a blue &ldquo;unrecognised
                app&rdquo; screen the first time you run it. That's expected, not a virus. Click{' '}
                <strong>More info</strong>, then <strong>Run anyway</strong>. If you downloaded the{' '}
                <code>.zip</code>, right-click it first → Properties → tick <strong>Unblock</strong>,
                then extract.
              </p>
            </div>
          </div>
        </Reveal>

        {/* Spotify API */}
        <Reveal as="section" className="section spotify">
          <h2 className="section__title">Go further with the Spotify API</h2>
          <p>
            Out of the box, Spytify+ pulls tags from Last.fm and works fine. But a free Spotify
            developer account is recommended for the full experience, and it only ever reads metadata,
            never the audio.
          </p>
          <ul className="ticks">
            <li>
              <Check className="tick__icon" size={18} strokeWidth={2.5} />
              <span>Sharper, more accurate tags, straight from Spotify.</span>
            </li>
            <li>
              <Check className="tick__icon" size={18} strokeWidth={2.5} />
              <span>"Record the current playlist as one album", cover art and all.</span>
            </li>
            <li>
              <Check className="tick__icon" size={18} strokeWidth={2.5} />
              <span>ISRC and Spotify track / album IDs written into every file.</span>
            </li>
          </ul>
          <p>
            It's a two-minute job: create an app in the{' '}
            <a href="https://developer.spotify.com/dashboard" target="_blank" rel="noreferrer">
              Spotify Developer Dashboard
            </a>
            , drop the Client ID and Secret into Spytify+'s Configuration, and hit Connect.
          </p>
        </Reveal>

        {/* Closing CTA */}
        <Reveal as="section" className="section cta-band">
          <h2 className="section__title">Take it for a spin</h2>
          <p>It's free and open. Grab the latest release, dig through the code, or build it yourself.</p>
          <div className="hero__cta">
            <a className="btn btn--primary" href={`${REPO}/releases/latest`} target="_blank" rel="noreferrer">
              <Download size={18} /> Latest release
            </a>
            <a className="btn btn--ghost" href={REPO} target="_blank" rel="noreferrer">
              <Github size={18} /> View on GitHub
            </a>
          </div>
        </Reveal>
      </main>

      <footer className="footer">
        <div className="footer__credit">
          Made and rebuilt by{' '}
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

      {/* Scroll-to-top FAB */}
      <AnimatePresence>
        {showTop && (
          <motion.button
            className="fab"
            aria-label="Back to top"
            onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
            initial={{ opacity: 0, scale: 0.6, y: 12 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.6, y: 12 }}
            transition={{ duration: 0.2, ease: EASE }}
            whileHover={{ y: -3 }}
            whileTap={{ scale: 0.92 }}
          >
            <ArrowUp size={22} />
          </motion.button>
        )}
      </AnimatePresence>

      {/* Lightbox */}
      <AnimatePresence>
        {zoom && (
          <motion.div
            className="lightbox"
            role="dialog"
            aria-modal="true"
            aria-label={zoom.alt}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={() => setZoom(null)}
          >
            <button className="lightbox__close" aria-label="Close image" autoFocus onClick={() => setZoom(null)}>
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
