let currentNodes = [];
let currentCaptures = [];
let selectedCaptureId = new URLSearchParams(window.location.search).get('captureId');

function showToast(msg) {
  const box = document.getElementById('toastBox');
  box.textContent = msg;
  box.style.display = 'block';
  setTimeout(() => {
    box.style.display = 'none';
  }, 3000);
}

function openHistoryInNewWindow() {
  window.open('/history/index.html', '_blank', 'noopener');
}

function fillNextDockerIp() {
  // Buscar el siguiente sufijo disponible empezando en 10.10.0.11
  const usedIps = new Set(currentNodes.map(n => n.assignedIp));
  let suffix = 11;
  while (usedIps.has(`10.10.0.${suffix}`)) {
    suffix++;
  }
  document.getElementById('assignedIp').value = `10.10.0.${suffix}`;
  document.getElementById('cameraIp').value = `10.20.1.${suffix}`;
  if (!document.getElementById('nodeName').value) {
    document.getElementById('nodeName').value = `Camara-Nodo-${String(suffix - 10).padStart(2, '0')}`;
  }
}

async function refreshAll() {
  try {
    const [summaryRes, capturesRes] = await Promise.all([
      fetch('/api/parent/summary'),
      fetch('/api/parent/captures?limit=30')
    ]);

    const summary = await summaryRes.json();
    const captures = await capturesRes.json();

    currentNodes = summary.childNodes || [];
    currentCaptures = captures || [];

    document.getElementById('parentIpBadge').textContent = summary.parentServerIp;
    document.getElementById('statOnlineNodes').textContent = `${summary.onlineChildNodes}/${summary.totalChildNodes}`;
    document.getElementById('statTotalCaptures').textContent = summary.totalCapturesStored;
    document.getElementById('statPollingCycles').textContent = summary.autoPolling.totalAutomatedCycles;
    document.getElementById('nodeCountLabel').textContent = currentNodes.length;

    if (document.activeElement !== document.getElementById('pollingInterval')) {
      document.getElementById('pollingEnabled').checked = summary.autoPolling.enabled;
      document.getElementById('pollingInterval').value = summary.autoPolling.intervalSeconds;
    }

    renderNodesTable();
    renderActiveCapture();
  } catch (err) {
    console.error('Error al actualizar estado:', err);
  }
}

function renderNodesTable() {
  const tbody = document.getElementById('nodesTableBody');
  if (currentNodes.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" class="empty-row">No hay servidores hijos registrados.</td></tr>`;
    return;
  }

  tbody.innerHTML = currentNodes.map(node => {
    const isOnline = node.status === 'Online' || node.status === 'Capturing';
    const tagClass = isOnline ? 'tag-ok' : 'tag-err';

    return `
      <tr>
        <td><strong>${node.name}</strong></td>
        <td class="mono">${node.assignedIp}:${node.port}</td>
        <td class="mono">${node.cameraIp}</td>
        <td class="mono">${node.parentIp}</td>
        <td><span class="${tagClass}">${node.status}</span></td>
        <td class="mono">${node.totalCaptures}</td>
        <td>
          <button type="button" class="btn btn-accent btn-xs" onclick="triggerSingleCapture('${node.id}', '${node.name}')">
            Capturar
          </button>
          <button type="button" class="btn btn-del btn-xs" onclick="deleteNode('${node.id}', '${node.name}')">
            Eliminar
          </button>
        </td>
      </tr>
    `;
  }).join('');
}

function renderActiveCapture() {
  if (currentCaptures.length === 0) {
    document.getElementById('framePreviewBox').innerHTML = `<span>Sin captura</span>`;
    document.getElementById('captureMetaInfo').innerHTML =
      `<div><strong>Estado:</strong> Crea un servidor hijo y solicita una captura para recibir su JSON.</div>`;
    document.getElementById('jsonViewer').textContent = '{}';
    window._lastFullCaptureJson = null;
    return;
  }

  if (!selectedCaptureId || !currentCaptures.some(c => c.captureId === selectedCaptureId)) {
    selectedCaptureId = currentCaptures[0].captureId;
  }

  const activeCapture = currentCaptures.find(c => c.captureId === selectedCaptureId) || currentCaptures[0];
  displayCaptureInViewer(activeCapture);
}

function displayCaptureInViewer(capture) {
  if (!capture) return;

  const frameBox = document.getElementById('framePreviewBox');
  frameBox.innerHTML = `<img src="data:${capture.imageMimeType};base64,${capture.imageBase64}" alt="Miniatura ${capture.childNodeName}" />`;

  const timeStr = new Date(capture.capturedAtUtc).toLocaleTimeString();
  document.getElementById('captureMetaInfo').innerHTML =
    `<div><strong>Servidor Hijo:</strong> ${capture.childNodeName} (<span class="mono">${capture.childAssignedIp}</span>)</div>` +
    `<div><strong>Camara IP:</strong> <span class="mono">${capture.cameraIp}</span> | <strong>Padre IP:</strong> <span class="mono">${capture.respondedToParentIp}</span></div>` +
    `<div><strong>Captura:</strong> ${timeStr} | <strong>Latencia:</strong> ${capture.latencyMs} ms | <strong>Origen:</strong> ${capture.triggerSource}</div>`;

  const displayJson = {
    ...capture,
    imageBase64: capture.imageBase64.substring(0, 64) + '... [BASE64_TRUNCADO_EN_VISTA]'
  };
  document.getElementById('jsonViewer').textContent = JSON.stringify(displayJson, null, 2);
  window._lastFullCaptureJson = JSON.stringify(capture, null, 2);
}

async function triggerSingleCapture(nodeId, nodeName) {
  showToast(`Solicitando captura a ${nodeName}...`);
  try {
    const res = await fetch(`/api/parent/nodes/${nodeId}/capture`, { method: 'POST' });
    const data = await res.json();
    if (!res.ok) {
      showToast(`Error: ${data.detail || data.error || 'Fallo al capturar'}`);
      return;
    }
    selectedCaptureId = data.captureId;
    showToast(`Respuesta JSON recibida de ${nodeName} (${data.childAssignedIp})`);
    await refreshAll();
  } catch (err) {
    showToast(`Error de red: ${err.message}`);
  }
}

async function triggerCaptureAll() {
  if (currentNodes.length === 0) {
    showToast('Primero debes crear al menos un servidor hijo.');
    return;
  }
  showToast('Solicitando captura a todos los servidores hijos...');
  try {
    const res = await fetch('/api/parent/nodes/capture-all', { method: 'POST' });
    const data = await res.json();
    if (data.captures && data.captures.length > 0) {
      selectedCaptureId = data.captures[0].captureId;
    }
    showToast(`${data.totalCaptured} servidores hijos respondieron.`);
    await refreshAll();
  } catch (err) {
    showToast(`Error en captura masiva: ${err.message}`);
  }
}

async function deleteNode(nodeId, nodeName) {
  try {
    const res = await fetch(`/api/parent/nodes/${nodeId}`, { method: 'DELETE' });
    if (res.ok) {
      showToast(`Servidor hijo ${nodeName} eliminado.`);
      await refreshAll();
    }
  } catch (err) {
    showToast(`Error al eliminar: ${err.message}`);
  }
}

async function savePollingConfig() {
  const enabled = document.getElementById('pollingEnabled').checked;
  const intervalSeconds = parseInt(document.getElementById('pollingInterval').value, 10) || 15;
  const res = await fetch('/api/parent/polling', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled, intervalSeconds })
  });
  if (res.ok) {
    showToast(`Poleo automatico ${enabled ? 'activo' : 'desactivado'} (${intervalSeconds}s)`);
    await refreshAll();
  }
}

function copyCurrentJson() {
  if (window._lastFullCaptureJson) {
    navigator.clipboard.writeText(window._lastFullCaptureJson);
    showToast('JSON copiado al portapapeles.');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('openHistoryBtn').addEventListener('click', openHistoryInNewWindow);
  document.getElementById('preset10Btn').addEventListener('click', fillNextDockerIp);
  document.getElementById('savePollingBtn').addEventListener('click', savePollingConfig);
  document.getElementById('captureAllBtn').addEventListener('click', triggerCaptureAll);
  document.getElementById('refreshBtn').addEventListener('click', refreshAll);
  document.getElementById('copyJsonBtn').addEventListener('click', copyCurrentJson);

  document.getElementById('createNodeForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const payload = {
      name: document.getElementById('nodeName').value.trim(),
      assignedIp: document.getElementById('assignedIp').value.trim(),
      cameraIp: document.getElementById('cameraIp').value.trim() || null,
      port: parseInt(document.getElementById('nodePort').value, 10) || 8080,
      provisioningMode: 'DockerContainer'
    };

    const res = await fetch('/api/parent/nodes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok) {
      showToast(`Error: ${data.error || 'No se pudo crear el servidor hijo'}`);
      return;
    }

    showToast(`Servidor hijo '${data.name}' creado con IP ${data.assignedIp}`);
    document.getElementById('nodeName').value = '';
    document.getElementById('assignedIp').value = '';
    document.getElementById('cameraIp').value = '';
    await refreshAll();
  });

  refreshAll();
  setInterval(refreshAll, 4000);
});
