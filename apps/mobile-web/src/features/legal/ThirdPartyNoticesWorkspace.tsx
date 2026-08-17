import { useEffect, useState } from "react";
import { ChevronLeft, ExternalLink, Scale } from "lucide-react";
import "./legal.css";

interface ThirdPartyNotice {
  name: string;
  license: string;
  source: string;
  text: string;
}

interface ThirdPartyNoticesWorkspaceProps {
  onBack: () => void;
}

export function ThirdPartyNoticesWorkspace({ onBack }: ThirdPartyNoticesWorkspaceProps) {
  const [notices, setNotices] = useState<ThirdPartyNotice[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    void fetch("./third-party-notices.txt", { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}.`);
        }
        return response.text();
      })
      .then((source) => {
        if (!cancelled) {
          const parsed = parseThirdPartyNotices(source);
          if (parsed.length === 0) {throw new Error("No valid third-party notices were found.");}
          setNotices(parsed);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError("The third-party notices could not be loaded. Please try again later.");
        }
      });

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, []);

  return (
    <section className="third-party-notices-workspace" aria-labelledby="third-party-notices-title" aria-busy={notices === null && error === null}>
      <header className="third-party-notices-header">
        <button type="button" className="third-party-notices-back" onClick={onBack}>
          <ChevronLeft aria-hidden="true" />
          <span>Back</span>
        </button>
        <div className="third-party-notices-heading">
          <span className="third-party-notices-eyebrow">LEGAL</span>
          <h1 id="third-party-notices-title">Third-party notices</h1>
        </div>
        <Scale aria-hidden="true" className="third-party-notices-icon" />
      </header>

      <div className="third-party-notices-scroll-region">
        <div className="third-party-notices-intro">
          <p>
            Voltura Air gratefully acknowledges the authors and contributors of the software below.
            No listed project or contributor endorses or is affiliated with Voltura Air or Voltura AB.
          </p>
          <p>Each component is provided under its own license and warranty disclaimer.</p>
        </div>

        {notices === null && error === null && <div className="third-party-notices-state" role="status">Loading notices…</div>}
        {error !== null && <div className="third-party-notices-state" role="alert">{error}</div>}
        {notices !== null && (
          <div className="third-party-notice-list">
            {notices.map((notice) => (
              <article className="third-party-notice-card" key={`${notice.name}-${notice.source}`}>
                <div className="third-party-notice-card-header">
                  <div>
                    <h2>{notice.name}</h2>
                    <span>{notice.license}</span>
                  </div>
                  <a href={notice.source} target="_blank" rel="noreferrer">
                    <span>Source</span>
                    <ExternalLink aria-hidden="true" />
                  </a>
                </div>
                <pre className="third-party-notice-license selectable-text">{notice.text}</pre>
              </article>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

export function parseThirdPartyNotices(source: string): ThirdPartyNotice[] {
  const normalized = source.replace(/\r\n?/gu, "\n");
  const notices: ThirdPartyNotice[] = [];
  const noticePattern = /(?:^|\n)-{8,}\n([^\n]+)\nLicense: ([^\n]+)\nSource: ([^\n]+)\n-{8,}\n([\s\S]*?)(?=\n-{8,}\n|$)/gu;

  for (const match of normalized.matchAll(noticePattern)) {
    const name = match[1]?.trim();
    const license = match[2]?.trim();
    const sourceUrl = match[3]?.trim();
    const text = match[4]?.trim();
    if (!name || !license || !sourceUrl || !text) {
      continue;
    }

    notices.push({ name, license, source: sourceUrl, text });
  }

  return notices;
}
