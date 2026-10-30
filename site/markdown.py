"""Small Markdown subset used for the site Help page and the manual PDF (from Grok's build_site.py)."""
import re, html

def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r'`([^`]+)`', r'<code>\1</code>', t)
    t = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'\[([^\]]+)\]\((https?://[^)]+)\)', r'<a href="\2">\1</a>', t)
    t = re.sub(r'(?<![">])(https?://[^\s<|)]+)', r'<a href="\1">\1</a>', t)
    return t

def md(text):
    out, lines, i = [], text.split('\n'), 0
    while i < len(lines):
        l = lines[i]
        if l.startswith('```'):
            j = i + 1
            while j < len(lines) and not lines[j].startswith('```'): j += 1
            out.append('<pre>' + html.escape('\n'.join(lines[i+1:j])) + '</pre>'); i = j + 1; continue
        m = re.match(r'(#{1,4}) (.*)', l)
        if m:
            n = len(m.group(1)); slug = re.sub(r'[^a-z0-9]+', '-', m.group(2).lower()).strip('-')
            out.append(f'<h{n} id="{slug}">{inline(m.group(2))}</h{n}>'); i += 1; continue
        if l.startswith('- '):
            out.append('<ul>')
            while i < len(lines) and lines[i].startswith('- '): out.append('<li>' + inline(lines[i][2:]) + '</li>'); i += 1
            out.append('</ul>'); continue
        if l.startswith('|'):
            rows = []
            while i < len(lines) and lines[i].startswith('|'): rows.append(lines[i]); i += 1
            cells = lambda r: [c.strip() for c in re.split(r'(?<!\\)\|', r.strip().strip('|'))]
            out.append('<table><tr>' + ''.join(f'<th>{inline(c)}</th>' for c in cells(rows[0])) + '</tr>')
            for r in rows[2:]: out.append('<tr>' + ''.join(f'<td>{inline(c.replace(chr(92)+"|","|"))}</td>' for c in cells(r)) + '</tr>')
            out.append('</table>'); continue
        if l.strip():
            p = [l]; i += 1
            while i < len(lines) and lines[i].strip() and not re.match(r'(#|- |\||```)', lines[i]): p.append(lines[i]); i += 1
            out.append('<p>' + inline(' '.join(p)) + '</p>'); continue
        i += 1
    return '\n'.join(out)
