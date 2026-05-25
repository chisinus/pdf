from pathlib import Path
p = Path('node_modules/pdfjs-dist/build/pdf.worker.min.mjs')
print('path exists', p.exists())
text = p.read_text(errors='ignore')
print('file length', len(text))
print('docId count', text.count('docId'))
print('WorkerMessageHandler count', text.count('WorkerMessageHandler'))
