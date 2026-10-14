import { useRef, useState, type ChangeEvent, type ReactNode } from "react";

type Screen = "inbox" | "review" | "summary";
type IconName =
  | "archive"
  | "arrow"
  | "check"
  | "chevron"
  | "clock"
  | "close"
  | "download"
  | "file"
  | "grid"
  | "help"
  | "image"
  | "menu"
  | "more"
  | "plus"
  | "report"
  | "settings"
  | "spark"
  | "upload";

function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  const paths: Record<IconName, ReactNode> = {
    archive: (
      <>
        <path d="M5 8v10h14V8" />
        <path d="M3 4h18v4H3zM9 12h6" />
      </>
    ),
    arrow: <path d="m9 18 6-6-6-6" />,
    check: <path d="m5 12 4 4L19 6" />,
    chevron: <path d="m8 10 4 4 4-4" />,
    clock: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3 2" />
      </>
    ),
    close: <path d="m7 7 10 10M17 7 7 17" />,
    download: (
      <>
        <path d="M12 3v12m-5-5 5 5 5-5" />
        <path d="M5 20h14" />
      </>
    ),
    file: (
      <>
        <path d="M6 3h8l4 4v14H6z" />
        <path d="M14 3v5h4M9 13h6M9 17h5" />
      </>
    ),
    grid: (
      <>
        <rect x="4" y="4" width="6" height="6" rx="1" />
        <rect x="14" y="4" width="6" height="6" rx="1" />
        <rect x="4" y="14" width="6" height="6" rx="1" />
        <rect x="14" y="14" width="6" height="6" rx="1" />
      </>
    ),
    help: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M9.8 9a2.3 2.3 0 1 1 3.2 2.1c-.7.4-1 1-1 1.9M12 17h.01" />
      </>
    ),
    image: (
      <>
        <rect x="3" y="4" width="18" height="16" rx="3" />
        <circle cx="9" cy="10" r="2" />
        <path d="m5 18 5-5 3 3 2-2 4 4" />
      </>
    ),
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
    more: (
      <>
        <circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" />
        <circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" />
        <circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" />
      </>
    ),
    plus: <path d="M12 5v14M5 12h14" />,
    report: (
      <>
        <path d="M6 20V10m6 10V4m6 16v-7" />
        <path d="M3 20h18" />
      </>
    ),
    settings: (
      <>
        <circle cx="12" cy="12" r="3" />
        <path d="M12 3v2m0 14v2M3 12h2m14 0h2M5.6 5.6 7 7m10 10 1.4 1.4m0-12.8L17 7M7 17l-1.4 1.4" />
      </>
    ),
    spark: <path d="m12 3 1.5 5.5L19 10l-5.5 1.5L12 17l-1.5-5.5L5 10l5.5-1.5z" />,
    upload: (
      <>
        <path d="M12 16V5m-5 5 5-5 5 5" />
        <path d="M5 19h14" />
      </>
    ),
  };

  return (
    <svg
      aria-hidden="true"
      className="icon"
      fill="none"
      height={size}
      viewBox="0 0 24 24"
      width={size}
    >
      {paths[name]}
    </svg>
  );
}

function Button({
  children,
  onClick,
  variant = "primary",
}: {
  children: ReactNode;
  onClick?: () => void;
  variant?: "primary" | "secondary" | "quiet" | "dark";
}) {
  return (
    <button className={`button button--${variant}`} onClick={onClick} type="button">
      {children}
    </button>
  );
}

function Brand() {
  return (
    <button className="brand" type="button">
      <span className="brand__mark">
        <span />
        <span />
      </span>
      <span>KuTax</span>
      <span className="brand__spark">✦</span>
    </button>
  );
}

function Header({
  screen,
  setScreen,
}: {
  screen: Screen;
  setScreen: (screen: Screen) => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <header className="header">
      <Brand />
      <nav aria-label="Main navigation" className="desktop-nav">
        <button
          className={screen === "inbox" ? "nav-item is-active" : "nav-item"}
          onClick={() => setScreen("inbox")}
          type="button"
        >
          Inbox
        </button>
        <button
          className={screen === "review" ? "nav-item is-active" : "nav-item"}
          onClick={() => setScreen("review")}
          type="button"
        >
          Review
          <span className="nav-badge">3</span>
        </button>
        <button
          className={screen === "summary" ? "nav-item is-active" : "nav-item"}
          onClick={() => setScreen("summary")}
          type="button"
        >
          Summary
        </button>
      </nav>
      <div className="header__tools">
        <button className="year-select" type="button">
          <span className="year-select__label">Tax year</span>
          <strong>FY 2025–26</strong>
          <Icon name="chevron" size={16} />
        </button>
        <button aria-label="Help" className="icon-button header-help" type="button">
          <Icon name="help" />
        </button>
        <button aria-label="Profile" className="avatar" type="button">
          AH
          <span className="status-dot" />
        </button>
        <div className="more-menu">
          <button
            aria-label="More options"
            className="icon-button"
            onClick={() => setMenuOpen(!menuOpen)}
            type="button"
          >
            <Icon name={menuOpen ? "close" : "menu"} />
          </button>
          {menuOpen && (
            <div className="menu-popover">
              <button onClick={() => setScreen("review")} type="button">
                <Icon name="clock" /> Review queue
              </button>
              <button onClick={() => setScreen("summary")} type="button">
                <Icon name="report" /> Reports & export
              </button>
              <button type="button">
                <Icon name="settings" /> Settings
              </button>
              <button type="button">
                <Icon name="help" /> Help centre
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}

function UploadArtwork() {
  return (
    <div className="upload-art" aria-hidden="true">
      <div className="orbit orbit--one" />
      <div className="orbit orbit--two" />
      <div className="upload-machine">
        <span className="machine-light" />
        <span className="machine-slot" />
        <div className="machine-screen">
          <Icon name="upload" size={32} />
        </div>
        <div className="machine-controls">
          <span />
          <span />
          <span />
        </div>
      </div>
      <div className="floating-file floating-file--left">
        <Icon name="file" />
        <span />
        <span />
      </div>
      <div className="floating-file floating-file--right">
        <Icon name="image" />
        <span />
      </div>
      <span className="tiny-spark spark-one">✦</span>
      <span className="tiny-spark spark-two">✦</span>
    </div>
  );
}

function UploadBoard() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [uploaded, setUploaded] = useState<string[]>([]);

  const collectFiles = (files: FileList | null) => {
    if (!files) return;
    setUploaded(Array.from(files).map((file) => file.name));
    setDragging(false);
  };

  return (
    <section
      className={dragging ? "upload-board is-dragging" : "upload-board"}
      onDragEnter={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(event) => {
        event.preventDefault();
        setDragging(false);
      }}
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => {
        event.preventDefault();
        collectFiles(event.dataTransfer.files);
      }}
    >
      <div className="board-grid" />
      <span className="board-label">
        <span className="live-dot" />
        Smart receipt inbox
      </span>
      <UploadArtwork />
      {uploaded.length ? (
        <div className="upload-copy upload-success">
          <span className="success-icon">
            <Icon name="check" size={28} />
          </span>
          <h2>{uploaded.length} {uploaded.length === 1 ? "file" : "files"} landed safely</h2>
          <p>KuTax is reading the details now. Nice work.</p>
          <div className="uploaded-files">
            {uploaded.slice(0, 2).map((file) => (
              <span key={file}>
                <Icon name="file" size={16} /> {file}
              </span>
            ))}
          </div>
          <Button onClick={() => setUploaded([])} variant="secondary">
            Add more
          </Button>
        </div>
      ) : (
        <div className="upload-copy">
          <span className="eyebrow">Drop it. Done.</span>
          <h2>Send your tax stuff this way</h2>
          <p>
            Drop receipts, invoices or supporting documents here.
            <br />
            We’ll sort out the details.
          </p>
          <div className="upload-actions">
            <Button onClick={() => inputRef.current?.click()}>
              <Icon name="plus" size={18} /> Browse files
            </Button>
            <span>or drag & drop anywhere here</span>
          </div>
          <div className="file-types">
            {["PDF", "JPG", "PNG", "HEIC"].map((type) => (
              <span key={type}>{type}</span>
            ))}
          </div>
        </div>
      )}
      <input
        accept=".pdf,.jpg,.jpeg,.png,.heic"
        hidden
        multiple
        onChange={(event: ChangeEvent<HTMLInputElement>) => collectFiles(event.target.files)}
        ref={inputRef}
        type="file"
      />
      <span className="privacy-note">
        <Icon name="check" size={14} /> Private & encrypted
      </span>
    </section>
  );
}

function StatStrip({ setScreen }: { setScreen: (screen: Screen) => void }) {
  return (
    <section className="stat-strip">
      <div className="stat-item">
        <span className="stat-icon stat-icon--blue">
          <Icon name="archive" />
        </span>
        <span>
          <small>Documents uploaded</small>
          <strong>24</strong>
        </span>
      </div>
      <div className="stat-divider" />
      <button className="stat-item stat-item--button" onClick={() => setScreen("review")} type="button">
        <span className="stat-icon stat-icon--orange">
          <Icon name="clock" />
        </span>
        <span>
          <small>Ready for a quick check</small>
          <strong>3 <em>Review now</em></strong>
        </span>
        <Icon name="arrow" />
      </button>
      <div className="stat-divider" />
      <div className="stat-item">
        <span className="stat-icon stat-icon--mint">
          <Icon name="spark" />
        </span>
        <span>
          <small>Draft deductions</small>
          <strong>$3,842.60</strong>
        </span>
      </div>
    </section>
  );
}

function ComponentPreview() {
  return (
    <section className="component-preview">
      <div>
        <span className="section-kicker">KuTax kit · v1.0</span>
        <h3>Friendly by design</h3>
        <p>A small, flexible system built to make serious tax tasks feel lighter.</p>
      </div>
      <div className="preview-components">
        <div className="preview-block">
          <small>Actions</small>
          <div className="preview-row">
            <Button>Primary action</Button>
            <Button variant="secondary">Secondary</Button>
            <button aria-label="More" className="icon-button" type="button"><Icon name="more" /></button>
          </div>
        </div>
        <div className="preview-block">
          <small>Tax year</small>
          <button className="year-select year-select--preview" type="button">
            <span className="year-select__label">Tax year</span>
            <strong>FY 2025–26</strong>
            <Icon name="chevron" size={16} />
          </button>
        </div>
        <div className="preview-block">
          <small>Surfaces</small>
          <div className="mini-cards">
            <span className="mini-card mini-card--yellow"><Icon name="file" /></span>
            <span className="mini-card mini-card--purple"><Icon name="spark" /></span>
            <span className="mini-upload"><Icon name="upload" /> Drop zone</span>
          </div>
        </div>
      </div>
    </section>
  );
}

function Inbox({ setScreen }: { setScreen: (screen: Screen) => void }) {
  return (
    <main>
      <section className="page-intro">
        <div>
          <span className="eyebrow">G’day, Alex</span>
          <h1>Your receipts have a happy place.</h1>
          <p>Send them in as you go. KuTax will read, sort and keep them ready for tax time.</p>
        </div>
        <div className="progress-pill">
          <span className="progress-pill__ring">68%</span>
          <span><small>Tax readiness</small><strong>Looking good</strong></span>
        </div>
      </section>
      <UploadBoard />
      <StatStrip setScreen={setScreen} />
      <ComponentPreview />
    </main>
  );
}

const receipts = [
  { category: "Work travel", date: "18 Jun 2026", merchant: "Metro Fuel Co.", price: "$86.42", type: "Fuel", color: "coral" },
  { category: "Home office", date: "12 Jun 2026", merchant: "Officeworks", price: "$214.90", type: "Equipment", color: "blue" },
  { category: "Other expenses", date: "04 Jun 2026", merchant: "Adobe", price: "$32.99", type: "Subscription", color: "purple" },
  { category: "Work travel", date: "29 May 2026", merchant: "CityLink Tolls", price: "$18.74", type: "Travel", color: "blue" },
  { category: "Self-education", date: "22 May 2026", merchant: "Readings Books", price: "$64.95", type: "Books", color: "coral" },
  { category: "Home office", date: "16 May 2026", merchant: "Telstra", price: "$89.00", type: "Internet", color: "purple" },
];

function ReviewQueue() {
  const [selected, setSelected] = useState(0);
  const [confirmed, setConfirmed] = useState<number[]>([]);
  const [queueCollapsed, setQueueCollapsed] = useState(false);
  const [query, setQuery] = useState("");
  const [queueFilter, setQueueFilter] = useState<"waiting" | "all">("waiting");
  const current = receipts[selected];
  const visibleReceipts = receipts.filter((receipt, index) => {
    const matchesQuery = `${receipt.merchant} ${receipt.type} ${receipt.price}`.toLowerCase().includes(query.toLowerCase());
    const matchesFilter = queueFilter === "all" || !confirmed.includes(index);
    return matchesQuery && matchesFilter;
  });
  const confirmCurrent = () => {
    const nextConfirmed = [...new Set([...confirmed, selected])];
    setConfirmed(nextConfirmed);
    const nextIndex = receipts.findIndex((_, index) => !nextConfirmed.includes(index));
    if (nextIndex !== -1) setSelected(nextIndex);
  };

  return (
    <main className="review-page">
      <section className="page-heading page-heading--with-action">
        <div>
          <span className="eyebrow">Review station · FY 2025–26</span>
          <h1>Check the capture.</h1>
          <p>Pick a document, compare the original, then confirm the extracted details.</p>
        </div>
        <span className="queue-progress"><strong>{confirmed.length}</strong> of {receipts.length} cleared</span>
      </section>
      <div className={queueCollapsed ? "review-layout queue-is-collapsed" : "review-layout"}>
        <aside className={queueCollapsed ? "receipt-list is-collapsed" : "receipt-list"}>
          <div className="panel-cap">
            <span>{queueCollapsed ? "01" : "01 / Queue"}</span>
            {!queueCollapsed && <small>{receipts.length - confirmed.length} waiting</small>}
            <button
              aria-label={queueCollapsed ? "Expand receipt queue" : "Collapse receipt queue"}
              className="queue-toggle"
              onClick={() => setQueueCollapsed(!queueCollapsed)}
              type="button"
            >
              <Icon name="chevron" size={15} />
            </button>
          </div>
          {!queueCollapsed && (
            <div className="queue-controls">
              <label className="queue-search">
                <Icon name="file" size={15} />
                <input
                  aria-label="Search receipt queue"
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Search merchant or amount"
                  value={query}
                />
              </label>
              <div className="queue-filters">
                <button
                  className={queueFilter === "waiting" ? "is-active" : ""}
                  onClick={() => setQueueFilter("waiting")}
                  type="button"
                >
                  Waiting {receipts.length - confirmed.length}
                </button>
                <button
                  className={queueFilter === "all" ? "is-active" : ""}
                  onClick={() => setQueueFilter("all")}
                  type="button"
                >
                  All {receipts.length}
                </button>
              </div>
            </div>
          )}
          <div className="queue-scroll">
            {visibleReceipts.map((receipt) => {
              const index = receipts.indexOf(receipt);
              return (
                <button
                  aria-label={`${receipt.merchant}, ${receipt.price}`}
                  className={`receipt-row ${selected === index ? "is-selected" : ""} ${confirmed.includes(index) ? "is-done" : ""}`}
                  key={receipt.merchant}
                  onClick={() => setSelected(index)}
                  title={queueCollapsed ? receipt.merchant : undefined}
                  type="button"
                >
                  <span className={`receipt-thumb receipt-thumb--${receipt.color}`}><Icon name="file" /></span>
                  <span className="receipt-row__copy">
                    <strong>{receipt.merchant}</strong>
                    <small>{receipt.date} · {receipt.type}</small>
                  </span>
                  <strong className="receipt-price">{receipt.price}</strong>
                  {confirmed.includes(index) && <span className="done-check"><Icon name="check" size={14} /></span>}
                </button>
              );
            })}
            {!visibleReceipts.length && (
              <div className="queue-empty">
                <Icon name="check" />
                <strong>Queue cleared</strong>
                <small>Nothing else needs review.</small>
              </div>
            )}
          </div>
          <div className="queue-foot">
            <span className="queue-meter"><i style={{ width: `${(confirmed.length / receipts.length) * 100}%` }} /></span>
            <small>{confirmed.length ? "Good progress" : "Start with any document"}</small>
          </div>
        </aside>
        <section className="review-card">
          <div className="receipt-preview">
            <div className="panel-cap document-cap">
              <span>02 / Original</span>
              <div className="document-tools">
                <button type="button">−</button>
                <small>100%</small>
                <button type="button">+</button>
              </div>
            </div>
            <span className="preview-tag"><span className="scan-light" /> Scan complete</span>
            <div className="paper-receipt">
              <span className="paper-logo">{current.merchant.slice(0, 1)}</span>
              <strong>{current.merchant}</strong>
              <small>TAX INVOICE</small>
              <div className="paper-lines"><span /><span /><span /><span /></div>
              <div className="paper-total"><span>TOTAL AUD</span><strong>{current.price}</strong></div>
              <div className="paper-barcode" />
            </div>
          </div>
          <div className="receipt-form">
            <div className="panel-cap">
              <span>03 / Extracted</span>
              <small>3 fields found</small>
            </div>
            <div className="form-heading">
              <div><span className="section-kicker">Document details</span><h2>{current.merchant}</h2></div>
              <button aria-label="More options" className="icon-button" type="button"><Icon name="more" /></button>
            </div>
            <label>Amount <span>Looks clear</span><input defaultValue={current.price} key={`${current.merchant}-price`} /></label>
            <label>Date <input defaultValue={current.date} key={`${current.merchant}-date`} /></label>
            <label>Deduction category <select defaultValue={current.category} key={`${current.merchant}-category`}><option>{current.category}</option><option>Work travel</option><option>Home office</option><option>Other expenses</option></select></label>
            <div className="ai-note"><span className="match-score">94</span><span><strong>High confidence match</strong><small>Consistent with your previous {current.type.toLowerCase()} claims.</small></span></div>
            <div className="form-actions">
              <Button variant="quiet">Not deductible</Button>
              <Button onClick={confirmCurrent}>
                <Icon name="check" size={18} /> Confirm receipt
              </Button>
            </div>
          </div>
        </section>
      </div>
    </main>
  );
}

function Summary() {
  return (
    <main>
      <section className="page-heading page-heading--with-action">
        <div>
          <span className="eyebrow">Tax summary · FY 2025–26</span>
          <h1>Your year, neatly sorted.</h1>
          <p>A clear draft of what KuTax has found so far. Ready when you are.</p>
        </div>
        <Button><Icon name="download" size={18} /> Export report</Button>
      </section>
      <section className="summary-hero">
        <div className="summary-total">
          <span className="section-kicker">Draft deductions</span>
          <strong>$3,842<span>.60</span></strong>
          <p>Across 24 documents</p>
          <div className="summary-status"><Icon name="check" size={16} /> All reviewed and ready</div>
        </div>
        <div className="summary-chart">
          <div className="chart-head"><span>Deduction mix</span><small>Updated today</small></div>
          <div className="donut"><span><strong>5</strong><small>categories</small></span></div>
          <div className="chart-legend">
            <span><i className="legend-dot legend-dot--purple" />Work travel <strong>$1,486</strong></span>
            <span><i className="legend-dot legend-dot--blue" />Home office <strong>$1,024</strong></span>
            <span><i className="legend-dot legend-dot--orange" />Education <strong>$742</strong></span>
            <span><i className="legend-dot legend-dot--mint" />Other <strong>$590.60</strong></span>
          </div>
        </div>
        <div className="mascot-card">
          <div className="mini-machine"><span /><Icon name="check" size={30} /></div>
          <span className="tiny-spark">✦</span>
          <strong>Nicely done!</strong>
          <p>Your tax pack is tidy, checked and ready to share.</p>
        </div>
      </section>
      <section className="report-section">
        <div className="report-section__heading"><div><span className="section-kicker">Breakdown</span><h2>Deductions by category</h2></div><Button variant="secondary">View all documents</Button></div>
        <div className="category-table">
          {[
            ["Work travel", "9 docs", "$1,486.20", "38%", "purple"],
            ["Home office", "6 docs", "$1,024.40", "27%", "blue"],
            ["Self-education", "4 docs", "$742.00", "19%", "orange"],
            ["Other work expenses", "5 docs", "$590.00", "16%", "mint"],
          ].map(([name, docs, amount, share, color]) => (
            <div className="category-row" key={name}>
              <span className={`category-icon category-icon--${color}`}><Icon name="archive" /></span>
              <span><strong>{name}</strong><small>{docs}</small></span>
              <div className="category-meter"><span style={{ width: share }} /></div>
              <strong>{amount}</strong>
              <Icon name="arrow" size={18} />
            </div>
          ))}
        </div>
      </section>
      <section className="export-strip">
        <span className="export-icon"><Icon name="report" /></span>
        <span><strong>Your accountant-ready report</strong><small>PDF summary + CSV line items + original documents</small></span>
        <Button variant="dark"><Icon name="download" size={18} /> Download tax pack</Button>
      </section>
    </main>
  );
}

export default function App() {
  const [screen, setScreen] = useState<Screen>("inbox");

  return (
    <div className="app-shell">
      <Header screen={screen} setScreen={setScreen} />
      {screen === "inbox" && <Inbox setScreen={setScreen} />}
      {screen === "review" && <ReviewQueue />}
      {screen === "summary" && <Summary />}
      <footer>
        <span>KuTax keeps your documents encrypted and stored in Australia.</span>
        <span>© 2026 KuTax · Privacy · Terms</span>
      </footer>
    </div>
  );
}
