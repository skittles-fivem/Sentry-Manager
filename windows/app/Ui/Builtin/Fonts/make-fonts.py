# Makes the TTFs in this folder from the web's fonts (web/fonts/*.woff2):
# WPF reads neither WOFF2 nor variable-font weights, so each weight the
# built-in interface uses is cut as a static TTF with "Inter" / "JetBrains
# Mono" as its family (name IDs 16/17 and 21/22) and the right weight class.
# Both fonts are SIL Open Font License 1.1, as shipped with the web pages.
#
#   pip install fonttools brotli
#   python make-fonts.py ../../../../web/fonts/inter-latin.woff2 Inter . 400,500,600,700
#   python make-fonts.py ../../../../web/fonts/jetbrains-mono-latin.woff2 "JetBrains Mono" . 400,600
import sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
src, fam, out = sys.argv[1], sys.argv[2], sys.argv[3]
weights = [int(w) for w in sys.argv[4].split(',')]
names = {100:'Thin',200:'ExtraLight',300:'Light',400:'Regular',500:'Medium',600:'SemiBold',700:'Bold',800:'ExtraBold',900:'Black'}
f0 = TTFont(src)
print([a.axisTag for a in f0['fvar'].axes] if 'fvar' in f0 else 'static')
for w in weights:
    f = TTFont(src)
    inst = instancer.instantiateVariableFont(f, {a.axisTag: (w if a.axisTag=='wght' else a.defaultValue) for a in f['fvar'].axes})
    sub = names[w]
    nt = inst['name']
    # typographic family = fam, legacy family = fam + ' ' + sub for non RIBBI
    for rec in list(nt.names):
        if rec.nameID in (1,2,4,6,16,17,21,22,25): nt.removeNames(nameID=rec.nameID)
    legacy_fam = fam if w in (400,700) else f'{fam} {sub}'
    legacy_sub = 'Bold' if w==700 else 'Regular'
    nt.setName(legacy_fam,1,3,1,0x409); nt.setName(legacy_sub,2,3,1,0x409)
    nt.setName(fam,16,3,1,0x409); nt.setName(sub,17,3,1,0x409); nt.setName(fam,21,3,1,0x409); nt.setName(sub,22,3,1,0x409)
    nt.setName(f'{fam} {sub}',4,3,1,0x409); nt.setName(f'{fam.replace(" ","")}-{sub}',6,3,1,0x409)
    inst['OS/2'].usWeightClass = w
    sel = inst['OS/2'].fsSelection & ~(0x1|0x20|0x40)
    sel |= 0x20 if w==700 else (0x40 if w==400 else 0)
    inst['OS/2'].fsSelection = sel
    inst['head'].macStyle = 1 if w==700 else 0
    inst.flavor = None
    inst.save(f'{out}/{fam.replace(" ","")}-{sub}.ttf')
    print('wrote', sub)
