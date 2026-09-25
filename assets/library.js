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
  const verificationOf = song => song.verification || {};
  const isReviewed = song => verificationOf(song).rhythm === 'reviewed';
  const isLogTested = song => verificationOf(song).log_playback === 'passed';
  const acquisitionOf = song => song.acquisition || 'direct-download';
  const workOf = song => song.work_id || song.id;
  const variantOf = song => song.variant_id || song.id;
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
      const bpmSearch = Number.isInteger(song.bpm) ? song.bpm : '速度待确认';
      return matchesFilter && `${song.title} ${(song.aliases || []).join(' ')} ${song.filename || ''} ${song.variant || ''} ${bpmSearch} ${workOf(song)} ${variantOf(song)} ${acquisitionOf(song)}`.toLocaleLowerCase().includes(query);
    });
  };
  function updateSelection() {
    document.getElementById('selected-count').textContent = `已选 ${selected.size} 个版本`;
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
    document.getElementById('result-count').textContent = `${visible.length} 个版本`;
    const empty = document.getElementById('empty');
    empty.hidden = visible.length !== 0;
    empty.textContent = filter === 'reviewed' ? '目前没有匹配的节奏复核版本。复核范围以每个版本的说明为准。' : filter === 'favorites' ? '这里还没有匹配的收藏。点击曲目旁的星标即可收藏。' : '没有匹配的曲目，试试曲名或 BPM。';
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
      const variantLabel = song.variant || variantOf(song);
      const defaultVariant = song.is_default_variant === true || (catalog.schema_version === 2 && song.is_default_variant === undefined);
      const duration = Number.isFinite(song.duration_seconds) && song.duration_seconds >= 0
        ? ` · ${Math.floor(song.duration_seconds / 60)}:${String(Math.round(song.duration_seconds) % 60).padStart(2, '0')}` : '';
      const format = acquisitionOf(song) === 'external-import'
        ? '外部导入' : song.legacy_compatible === false ? '桌面版下载' : 'TXT';
      titleCell.append(title, node('div', `${variantLabel}${defaultVariant ? ' · 默认版本' : ''}${duration} · ${format}`, 'song-version'));
      titleCell.title = song.filename || `${song.title} (${variantLabel})`;
      const bpmCell = node('td');
      const bpmKnown = Number.isInteger(song.bpm);
      const bpm = node('span', bpmKnown ? String(song.bpm) : '速度待确认', 'bpm');
      if (bpmKnown) bpm.append(node('small', 'BPM'));
      bpmCell.append(bpm);
      const stateCell = node('td');
      const verification = verificationOf(song);
      const reviewedLabel = acquisitionOf(song) === 'external-import' ? '已复核片段' : '节奏已复核';
      const rhythmBadge = node('span', isReviewed(song) ? reviewedLabel : verification.rhythm === 'deferred' ? '暂缓校对' : '参考谱 · 未校准', `review-badge${isReviewed(song) ? ' reviewed' : ''}`);
      if (song.review_note) rhythmBadge.title = song.review_note;
      stateCell.append(rhythmBadge);
      stateCell.append(node('div', isLogTested(song) ? '导入 / 日志通过' : verification.app_import === 'passed' ? '导入通过 · 日志待测' : verification.conversion === 'passed' ? '本地转换 / 解析通过' : '导入 / 日志待测', 'song-version'));
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
      if (song.source_url) actions.append(sourceLink('来源 ↗', song.source_url));
      if (acquisitionOf(song) === 'external-import') {
        if (song.reference && song.reference.url && song.reference.url !== song.source_url) {
          actions.append(sourceLink('导入参考 ↗', song.reference.url));
        }
        actions.append(node('span', '外部导入', 'song-version'));
      } else if (song.download_url) {
        actions.append(sourceLink(song.legacy_compatible === false ? '来源文件 ↗' : '原始 TXT ↗', song.download_url));
      }
      actions.append(star);
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
      songs: songs.filter(song => selected.has(song.id)).map(song => {
        const fields = ['id', 'title', 'filename', 'bpm', 'version', 'source_url', 'download_url', 'sha256',
          'git_blob_sha1', 'size', 'source_repository', 'source_commit', 'source_path', 'redistribution',
          'verified_sha256', 'reference', 'verification'];
        const record = Object.fromEntries(fields.filter(field => song[field] !== undefined).map(field => [field, song[field]]));
        record.work_id = workOf(song);
        record.variant_id = variantOf(song);
        record.variant = song.variant || variantOf(song);
        record.is_default_variant = song.is_default_variant === true || (catalog.schema_version === 2 && song.is_default_variant === undefined);
        record.acquisition = acquisitionOf(song);
        return record;
      })
    };
    const url = URL.createObjectURL(new Blob([JSON.stringify(selection, null, 2) + '\n'], {type: 'application/json;charset=utf-8'}));
    const link = node('a');
    link.href = url;
    link.download = 'harmonica-source-selection.json';
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    feedback.textContent = `已生成 ${selected.size} 个版本的来源清单，请查看浏览器下载。外部导入曲目需在本机导入，清单不含谱面。`;
  });
  const workCount = new Set(songs.map(workOf)).size;
  const variantCount = new Set(songs.map(song => `${workOf(song)}\0${variantOf(song)}`)).size;
  document.getElementById('total-stat').textContent = String(workCount).padStart(2, '0');
  document.getElementById('nav-work-count').textContent = String(workCount);
  document.getElementById('total-stat').nextElementSibling.textContent = `部作品 · 共 ${variantCount} 个版本`;
  document.getElementById('log-tested-stat').textContent = String(songs.filter(isLogTested).length).padStart(2, '0');
  document.getElementById('reviewed-stat').textContent = String(songs.filter(isReviewed).length).padStart(2, '0');
  document.getElementById('log-tested-stat').nextElementSibling.textContent = '日志通过版本';
  document.getElementById('reviewed-stat').nextElementSibling.textContent = '节奏复核版本';
  render();
})();
