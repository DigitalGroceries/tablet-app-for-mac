"""R18: list the app build's third-party packages with licence and full licence text (for the user / Claude)."""
import json,os,glob,re,html,sys
a=json.load(open('OpenTabletArtist/obj/project.assets.json'))
root=os.path.expanduser('~/.nuget/packages')
rows=[]
for k in sorted(x for x,v in a['libraries'].items() if v['type']=='package'):
    n,v=k.split('/'); d=f"{root}/{n.lower()}/{v.lower()}"
    t=open(glob.glob(d+'/*.nuspec')[0],encoding='utf-8-sig').read()
    m=re.search(r'<license type="(\w+)"[^>]*>([^<]+)</license>',t); lic=f"{m.group(1)}:{m.group(2)}" if m else ''
    m=re.search(r'<projectUrl>([^<]+)',t); proj=m.group(1) if m else ''
    files=[os.path.basename(f) for f in glob.glob(d+'/*') if re.search(r'licen[cs]e|copying|notice',os.path.basename(f),re.I)]
    who=(re.search(r'<copyright>([^<]+)',t) or re.search(r'<authors>([^<]+)',t))
    who=re.sub(r'^(Copyright\s*)?(\(c\)|©)\s*','',html.unescape(who.group(1)) if who else n+' authors',flags=re.I)
    rows.append((n,v,lic,proj,files,d,who))
other=lambda n: re.search(r'\.(Linux|Win32|WebAssembly|X11)$|Angle\.Windows|FreeDesktop|WaylandNET|Tmds\.DBus',n)
out=['# Tablet App for Mac – third-party packages and licences','',
'Generated on 2026-10-22 from the resolved dependencies of the app build (OpenTabletArtist/obj/project.assets.json, branch r12, R18 work in progress) and the package metadata in the local NuGet cache. "Mac build" says whether the package is used on macOS: "no" means it is referenced for another platform (Windows, Linux, browser); its managed part may still be inside the single-file binary, but its native libraries are not.','',
'## Summary','','| Name | Version | Licence | URL | Mac build |','|---|---|---|---|---|',
'| OpenTabletDriver (driver, daemon and plugin API; git submodule external/OpenTabletDriver) | pinned submodule | LGPL-3.0 | https://github.com/OpenTabletDriver/OpenTabletDriver | yes |',
'| OpenTabletArtist (the app this is a modified version of) | fork base | GPL-3.0 | https://github.com/TheSevenPens/OpenTabletArtist | yes |',
'| .NET runtime (Microsoft.NETCore.App, self-contained) | 10.0.13 | MIT | https://dot.net/ | yes |']
for n,v,lic,proj,files,d,who in rows:
    l=lic.split(':',1)[1] if lic.startswith('expression') else ('see licence file' if lic else ('Apache-2.0' if n=='HidSharpCore' else 'see package'))
    out.append(f'| {n} | {v} | {l} | {html.unescape(proj).split("?")[0] or "https://www.nuget.org/packages/"+n} | {"no" if other(n) else "yes"} |')
out+=['','Also bundled (docs/THIRD_PARTY_NOTICES.md): the OpenTabletArtist Dynamics plugin (first-party, GPL-3.0). The Windows-only Windows Ink plugin (GPL-3.0) and VMulti package (MIT) are not part of the Mac build.','','## Full licence texts','']
seen={}
def add(title,path):
    if os.path.isdir(path): path=glob.glob(path+'/*')[0]
    text=open(path,encoding='utf-8',errors='replace').read().rstrip()
    if text in seen:   # identical file already printed: refer to it instead of repeating it
        out.extend([f'### {title}','',f'Identical to "{seen[text]}" above.','']); return
    seen[text]=title
    out.extend([f'### {title}','','```',text,'```',''])
add('OpenTabletDriver: GNU Lesser General Public License v3.0','external/OpenTabletDriver/LICENSE')
add('OpenTabletArtist and Tablet App for Mac: GNU General Public License v3.0 (also required by LGPL-3.0)','LICENSE')
add('NOTICE (modified-version notice)','NOTICE')
rt=glob.glob(root+'/microsoft.netcore.app.runtime.osx-arm64/*/')[0]
add('.NET runtime: MIT',rt+'LICENSE.TXT'); add('.NET runtime: third-party notices',rt+'THIRD-PARTY-NOTICES.TXT')
MIT=open(rt+'LICENSE.TXT').read()
MIT=re.sub(r'Copyright \(c\)[^\n]*','Copyright (c) {who}',MIT)
ISC='ISC License\n\nCopyright (c) {who}\n\nPermission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.\n\nTHE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.'
for n,v,lic,proj,files,d,who in rows:
    lf=[f for f in files if re.search(r'licen',f,re.I)]
    if lf: add(f'{n} {v}',d+'/'+lf[0])
    elif 'ISC' in lic: out.extend([f'### {n} {v}','','```',ISC.format(who=who),'```',''])
    elif 'MIT' in lic: out.extend([f'### {n} {v}','',"(No licence file in the package; the standard MIT text with the package's copyright line.)",'','```',MIT.replace('{who}',who),'```',''])
    elif n=='HidSharpCore': add(f'{n} {v} (Apache-2.0; text from github.com/OpenTabletDriver/HIDSharpCore, the package ships none)','tools/licences/HidSharpCore-LICENSE.txt')
    else: out.extend([f'### {n} {v}','','No licence file or expression in the package.',''])
    for f in files:
        if f not in lf: add(f'{n} {v}: {f}',d+'/'+f)
open(sys.argv[1],'w').write('\n'.join(out))
