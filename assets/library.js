(() => {
  'use strict';
  const catalog = window.HARMONICA_CATALOG;
  const feedback = document.getElementById('feedback');
  if (!catalog || !Array.isArray(catalog.songs)) {
    feedback.textContent = '没有找到曲谱索引。请完整解压使用包，保留 assets 文件夹。';
    return;
  }
  const songs = catalog.songs;
  const validIds = new Set(songs.map(song => song.id));
  const storageKey = 'harmonica-friends-favorites-v1';
  let favorites = new Set();
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) || '[]');
    if (Array.isArray(stored)) favorites = new Set(stored.filter(id => validIds.has(id)));
  } catch { /* A restricted file/browser session can still use the catalogue. */ }
  const selected = new Set();
  let filter = 'all';
  const tbody = document.getElementById('songs');
  const search = document.getElementById('search');
  const selectVisible = document.getElementById('select-visible');
  const exportButton = document.getElementById('export-selection');
  const clearButton = document.getElementById('clear-selection');
  const isReviewed = song => song.verification.rhythm === 'reviewed' && song.verification.app_import === 'passed';
  const isLogTested = song => song.verification.log_playback === 'passed';
  const node = (tag, text, className) => {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    if (className) element.className = className;
    return element;
  };
  const visibleSongs = () => {
    const query = search.value.trim().toLocaleLowerCase();
    return songs.filter(song => {
      const matchesFilter = filter === 'all' || (filter === 'sample' && song.sample) || (filter === 'log-tested' && isLogTested(song)) || (filter === 'reviewed' && isReviewed(song)) || (filter === 'favorites' && favorites.has(song.id));
      return matchesFilter && `${song.title} ${(song.aliases || []).join(' ')} ${song.filename} ${song.bpm}`.toLocaleLowerCase().includes(query);
    });
  };
  function updateSelection() {
    document.getElementById('selected-count').textContent = `已选 ${selected.size} 首`;
    exportButton.disabled = selected.size === 0;
    clearButton.disabled = selected.size === 0;
    const visible = visibleSongs();
    const count = visible.filter(song => selected.has(song.id)).length;
    selectVisible.checked = visible.length > 0 && count === visible.length;
    selectVisible.indeterminate = count > 0 && count < visible.length;
    selectVisible.disabled = visible.length === 0;
  }
  function sourceLink(label, url) {
    const anchor = node('a', label);
    anchor.href = url;
    anchor.target = '_blank';
    anchor.rel = 'noopener noreferrer';
    return anchor;
  }
  function render() {
    tbody.replaceChildren();
    const visible = visibleSongs();
    document.getElementById('result-count').textContent = `${visible.length} 首`;
    const empty = document.getElementById('empty');
    empty.hidden = visible.length !== 0;
    empty.textContent = filter === 'reviewed' ? '目前没有完成导入验证和节奏校对的推荐曲目。可先查看“首轮 3 首”。' : filter === 'favorites' ? '这里还没有匹配的收藏。点击曲目旁的星标即可收藏。' : '没有匹配的曲目，试试曲名或 BPM。';
    for (const song of visible) {
      const row = node('tr');
      const checkCell = node('td', undefined, 'check-col');
      const check = node('input');
      check.type = 'checkbox';
      check.checked = selected.has(song.id);
      check.setAttribute('aria-label', `选择${song.title}`);
      check.addEventListener('change', () => { check.checked ? selected.add(song.id) : selected.delete(song.id); updateSelection(); });
      checkCell.append(check);
      const titleCell = node('td');
      const title = node('div', song.title, 'song-name');
      if (song.sample) title.append(node('span', '首轮', 'sample-badge'));
      titleCell.append(title, node('div', `${song.variant} · ${Math.floor((song.duration_seconds || 0) / 60)}:${String(Math.round(song.duration_seconds || 0) % 60).padStart(2, '0')} · ${song.legacy_compatible === false ? '桌面版下载' : 'TXT'}`, 'song-version'));
      titleCell.title = song.filename;
      const bpmCell = node('td');
      const bpm = node('span', String(song.bpm), 'bpm');
      bpm.append(node('small', 'BPM'));
      bpmCell.append(bpm);
      const stateCell = node('td');
      stateCell.append(node('span', isReviewed(song) ? '已校对' : song.verification.rhythm === 'source-timing-verified' ? '来源时值已核对' : '待音乐校对', `review-badge${isReviewed(song) ? ' reviewed' : ''}`));
      stateCell.append(node('div', isLogTested(song) ? '导入 / 日志通过' : song.verification.app_import === 'passed' ? '导入通过 · 日志待测' : song.verification.conversion === 'passed' ? '本地转换 / 解析通过' : '导入 / 日志待测', 'song-version'));
      const actionCell = node('td');
      const actions = node('div', undefined, 'row-actions');
      const star = node('button', favorites.has(song.id) ? '★' : '☆', 'favorite');
      star.type = 'button';
      star.setAttribute('aria-pressed', String(favorites.has(song.id)));
      star.setAttribute('aria-label', `${favorites.has(song.id) ? '取消收藏' : '收藏'}${song.title}`);
      star.addEventListener('click', () => {
        favorites.has(song.id) ? favorites.delete(song.id) : favorites.add(song.id);
        try { localStorage.setItem(storageKey, JSON.stringify([...favorites])); } catch { feedback.textContent = '浏览器限制了本地存储；收藏在本次打开期间仍可使用。'; }
        render();
      });
      actions.append(sourceLink('来源', song.source_url), sourceLink(song.legacy_compatible === false ? '来源文件 ↗' : '原始 TXT ↗', song.download_url), star);
      actionCell.append(actions);
      row.append(checkCell, titleCell, bpmCell, stateCell, actionCell);
      tbody.append(row);
    }
    updateSelection();
  }
  document.querySelectorAll('[data-filter]').forEach(button => button.addEventListener('click', () => {
    filter = button.dataset.filter;
    document.querySelectorAll('[data-filter]').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
    render();
  }));
  search.addEventListener('input', render);
  selectVisible.addEventListener('change', () => {
    for (const song of visibleSongs()) selectVisible.checked ? selected.add(song.id) : selected.delete(song.id);
    render();
  });
  clearButton.addEventListener('click', () => { selected.clear(); render(); });
  exportButton.addEventListener('click', () => {
    const selection = {
      schema_version: 1,
      type: 'harmonica-source-selection',
      generated_at: new Date().toISOString(),
      notice: '来源清单，不含谱面；不表示已下载、已导入或已校对。',
      song_ids: songs.filter(song => selected.has(song.id)).map(song => song.id),
      songs: songs.filter(song => selected.has(song.id)).map(({id, title, filename, bpm, version, source_url, download_url, sha256, verification}) => ({id, title, filename, bpm, version, source_url, download_url, sha256, verification}))
    };
    const url = URL.createObjectURL(new Blob([JSON.stringify(selection, null, 2) + '\n'], {type: 'application/json;charset=utf-8'}));
    const link = node('a');
    link.href = url;
    link.download = 'harmonica-source-selection.json';
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    feedback.textContent = `已生成 ${selected.size} 首的来源清单，请查看浏览器下载。曲谱仍需从原始来源获取。`;
  });
  document.getElementById('total-stat').textContent = String(songs.length).padStart(2, '0');
  document.getElementById('log-tested-stat').textContent = String(songs.filter(isLogTested).length).padStart(2, '0');
  document.getElementById('reviewed-stat').textContent = String(songs.filter(isReviewed).length).padStart(2, '0');
  render();
})();
