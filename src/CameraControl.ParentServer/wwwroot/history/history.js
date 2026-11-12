let capturesList = [];
let selectedCaptureId = null;

function showToast(msg) {
  const box = document.getElementById('toastBox');
  box.textContent = msg;
  box.style.display = 'block';
  setTimeout(() => {
    box.style.display = 'none';
  }, 3000);
}

async function loadNodesFilter() {
  try {
    const res = await fetch('/api/parent/nodes');
    const nodes = await res.json();
    const select = document.getElementById('nodeFilterSelect');
    const currentVal = select.value;

    select.innerHTML = `<option value="">Todos los servidores hijos</option>` +
      nodes.map(n => `<option value="${n.id}">${n.name} (${n.assignedIp})</option>`).join('');

    select.value = currentVal;
  } catch (err) {
    console.error('Error cargando filtro de nodos:', err);
  }
}

async function loadHistory() {
  try {
    const nodeId = document.getElementById('nodeFilterSelect').value;
    const url = nodeId
      ? `/api/parent/captures?limit=100&childNodeId=${encodeURIComponent(nodeId)}`
      : `/api/parent/captures?limit=100`;

    const res = await fetch(url);
    capturesList = await res.json();

    document.getElementById('totalCountBadge').textContent = capturesList.length;
    renderTable();

    if (capturesList.length > 0) {
      if (!selectedCaptureId || !capturesList.some(c => c.captureId === selectedCaptureId)) {
        selectedCaptureId = capturesList[0].captureId;
      }
      selectCapture(selectedCaptureId);
    } else {
      document.getElementById('detailPreviewBox').innerHTML =
        `<span class="empty-thumb">Sin captura</span>`;
      document.getElementById('detailMetaBox').textContent = 'No hay capturas para mostrar.';
      document.getElementById('detailJsonViewer').textContent = '{}';
    }
  } catch (err) {
    console.error('Error cargando historial:', err);
  }
}

function renderTable() {
  const tbody = document.getElementById('historyTableBody');
  if (capturesList.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" class="muted-cell">Sin capturas registradas.</td></tr>`;
    return;
  }

  tbody.innerHTML = capturesList.map(c => {
    const timeStr = new Date(c.capturedAtUtc).toLocaleString();
    const rowClass = c.captureId === selectedCaptureId ? 'selected-row' : '';
    return `
      <tr class="${rowClass}">
        <td class="mono">${timeStr}</td>
        <td><strong>${c.childNodeName}</strong></td>
        <td class="mono">${c.childAssignedIp}</td>
        <td class="mono">${c.cameraIp}</td>
        <td>${c.triggerSource}</td>
        <td class="mono">${c.latencyMs} ms</td>
        <td>
          <button type="button" class="btn btn-primary btn-sm" onclick="selectCapture('${c.captureId}')">
            Ver JSON
          </button>
        </td>
      </tr>
    `;
  }).join('');
}

function selectCapture(captureId) {
  selectedCaptureId = captureId;
  renderTable();

  const capture = capturesList.find(c => c.captureId === captureId);
  if (!capture) return;

  document.getElementById('detailPreviewBox').innerHTML =
    `<img src="data:${capture.imageMimeType};base64,${capture.imageBase64}" alt="Miniatura ${capture.childNodeName}" />`;

  document.getElementById('detailMetaBox').innerHTML =
    `<div><strong>ID Captura:</strong> <span class="mono">${capture.captureId.substring(0, 12)}...</span></div>` +
    `<div><strong>Servidor Hijo:</strong> ${capture.childNodeName}</div>` +
    `<div><strong>IP Hijo:</strong> <span class="mono">${capture.childAssignedIp}</span></div>` +
    `<div><strong>IP Camara:</strong> <span class="mono">${capture.cameraIp}</span></div>` +
    `<div><strong>Respondio a Padre IP:</strong> <span class="mono">${capture.respondedToParentIp}</span></div>` +
    `<div><strong>Origen:</strong> ${capture.triggerSource} | <strong>Latencia:</strong> ${capture.latencyMs} ms</div>`;

  document.getElementById('openInMainBtn').href = `/dashboard/index.html?captureId=${encodeURIComponent(capture.captureId)}`;

  const displayJson = {
    ...capture,
    imageBase64: capture.imageBase64.substring(0, 64) + '... [BASE64_TRUNCADO_EN_VISTA]'
  };
  document.getElementById('detailJsonViewer').textContent = JSON.stringify(displayJson, null, 2);
  window._selectedFullJson = JSON.stringify(capture, null, 2);
}

function copySelectedJson() {
  if (window._selectedFullJson) {
    navigator.clipboard.writeText(window._selectedFullJson);
    showToast('JSON copiado al portapapeles.');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('nodeFilterSelect').addEventListener('change', loadHistory);
  document.getElementById('refreshHistoryBtn').addEventListener('click', loadHistory);
  document.getElementById('copySelectedJsonBtn').addEventListener('click', copySelectedJson);

  loadNodesFilter();
  loadHistory();
  setInterval(loadHistory, 5000);
});
