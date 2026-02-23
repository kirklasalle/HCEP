import zipfile
import xml.etree.ElementTree as ET
import os

def extract_docx(filepath):
    parts = []
    try:
        with zipfile.ZipFile(filepath, 'r') as z:
            if 'word/document.xml' in z.namelist():
                root = ET.fromstring(z.read('word/document.xml'))
                for e in root.iter():
                    if e.text:
                        parts.append(e.text)
    except Exception as ex:
        parts.append(f'[ERROR: {ex}]')
    return ' '.join(parts)

def extract_xlsx(filepath):
    parts = []
    try:
        with zipfile.ZipFile(filepath, 'r') as z:
            if 'xl/sharedStrings.xml' in z.namelist():
                root = ET.fromstring(z.read('xl/sharedStrings.xml'))
                for e in root.iter():
                    if e.text:
                        parts.append(e.text)
    except Exception as ex:
        parts.append(f'[ERROR: {ex}]')
    return ' '.join(parts)

research_dir = r'D:\Projects\HCEP\HCEP-research'
output_file = os.path.join(research_dir, 'extracted_text.txt')
sections = []

for filename in sorted(os.listdir(research_dir)):
    filepath = os.path.join(research_dir, filename)
    if filename.endswith('.docx'):
        text = extract_docx(filepath)
        sep = '=' * 80
        sections.append(f'\n{sep}\n=== {filename} ===\n{sep}\n{text}\n')
        print(f'Extracted: {filename} ({len(text)} chars)')
    elif filename.endswith('.xlsx'):
        text = extract_xlsx(filepath)
        sep = '=' * 80
        sections.append(f'\n{sep}\n=== {filename} ===\n{sep}\n{text}\n')
        print(f'Extracted: {filename} ({len(text)} chars)')

with open(output_file, 'w', encoding='utf-8') as f:
    f.write('\n'.join(sections))

print(f'\nWrote {len(sections)} documents to: {output_file}')
