"""Regenerate the bundled UI-only subsets from installed Noto Sans JP static fonts."""
from pathlib import Path
import os
from fontTools import subset

root = Path(__file__).resolve().parents[3]
destination = Path(__file__).resolve().parent
text = (root / 'src/Hsis.Bootstrap/Bootstrap.cpp').read_text(encoding='utf-8')
source = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Fonts'
for weight in ('Regular', 'SemiBold'):
    options = subset.Options()
    options.name_IDs = ['*']
    options.name_legacy = True
    options.name_languages = ['*']
    font = subset.load_font(str(source / f'NotoSansJP-{weight}.ttf'), options)
    selector = subset.Subsetter(options=options)
    selector.populate(text=text)
    selector.subset(font)
    output = destination / f'NotoSansJP-{weight}.ttf'
    subset.save_font(font, str(output), options)
    print(output.name, output.stat().st_size)
