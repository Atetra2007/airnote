"""Compare source timing with the desktop conversion audit, without publishing score bodies.

Usage: python scripts/audit_community.py INDEX CACHE DESKTOP_REPORT
Run CatalogAudit first; the cache and report contain local-only downloaded content.
This independent check uses rational arithmetic to detect dropped rests, octave
changes, reordered events and rhythmic rounding. It is not a listening review.
"""
import hashlib
import json
from fractions import Fraction
from pathlib import Path
import re
import sys


def source_events(text):
    result = []
    tied = False
    for word in re.split(r'[\s|]+', text.strip()):
        if not word:
            continue
        word = word.translate(str.maketrans({'—': '-', '–': '-', '·': '.', '。': '.'}))
        if word == '~':
            assert result and result[-1][0] is not None and not tied
            tied = True
            continue
        if set(word) == {'-'}:
            assert result and not tied
            result[-1][1] += len(word)
            continue
        trailing_tie = word.endswith('~')
        if trailing_tie:
            word = word[:-1]
        match = re.fullmatch(r'([#b♯♭]?)(,*)([0-7])(\'*)(.*)', word)
        assert match, word
        accidental, low, degree, high, rhythm = match.groups()
        degree = int(degree)
        assert not (low and high)
        assert degree != 0 or not (accidental or low or high)
        midi = None if degree == 0 else [60, 62, 64, 65, 67, 69, 71][degree - 1] + 12 * (len(high) - len(low)) + {'': 0, '#': 1, '♯': 1, 'b': -1, '♭': -1}[accidental]
        if rhythm.startswith(':'):
            beats = Fraction(rhythm[1:])
        else:
            parts = re.fullmatch(r'(_*)(\.*)(-*)', rhythm)
            assert parts, word
            halves, dots, extension = map(len, parts.groups())
            beats = Fraction(1, 2**halves) * sum((Fraction(1, 2**i) for i in range(dots + 1)), Fraction(0)) + extension
        assert beats > 0
        if tied:
            assert result and midi is not None and result[-1][0] == midi
            result[-1][1] += beats
        else:
            result.append([midi, beats])
        tied = trailing_tie
    assert not tied
    return result


def audit(index_path, cache_path, report_path):
    catalog = json.loads(Path(index_path).read_text(encoding='utf-8'))
    report = {s['id']: s for s in json.loads(Path(report_path).read_text(encoding='utf-8'))}
    count = 0
    for song in catalog['songs']:
        if song.get('download_format') != 'delta-music/1':
            continue
        raw = (Path(cache_path) / (song['id'] + '.raw')).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == song['sha256'], song['id']
        source = json.loads(raw.decode('utf-8-sig'))
        actual = report[song['id']]
        if actual['status'] != 'passed':
            continue
        expected = source_events(source['jianpu'])
        assert len(expected) == len(actual['event_list']), song['id']
        for position, ((pitch, beats), observed) in enumerate(zip(expected, actual['event_list'])):
            assert pitch == observed['midi'] and abs(float(beats) - observed['beats']) < 1e-9, (song['id'], position)
        total = sum((event[1] for event in expected), Fraction(0))
        assert abs(float(total) - actual['beats']) < 1e-8
        assert abs(float(total * 60 / source['bpm']) - actual['duration_seconds']) < 1e-8
        count += 1
    print(f'PASS: {count} community sources; every pitch, rest, duration and event order matches the desktop conversion.')
    return count


if __name__ == '__main__':
    audit(*sys.argv[1:])
