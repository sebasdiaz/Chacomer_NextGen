#!/usr/bin/env node
// ---------------------------------------------------------------------------
// Genera las paginas de docs/wiki/_generado a partir del codigo.
//
// Lo que sale de aca NO se edita a mano: se regenera. Por eso no puede mentir.
// Lo que explica el porque de cada cosa vive en las paginas escritas a mano,
// que enlazan a estas.
//
//   node docs/wiki/generate.mjs            regenera
//   node docs/wiki/generate.mjs --check    exit 1 si el codigo cambio y esto no
//
// Sin dependencias: se corre con node, sin npm install.
//
// La salida es deterministica a proposito (nada de fechas ni orden de
// filesystem): si el codigo no cambio, regenerar no produce diff.
// ---------------------------------------------------------------------------

import { readFileSync, writeFileSync, readdirSync, statSync, existsSync, mkdirSync } from 'node:fs';
import path from 'node:path';

const OUT = 'docs/wiki/_generado';
const CHECK = process.argv.includes('--check');

const read = (p) => readFileSync(p, 'utf8');
const posix = (p) => p.split(path.sep).join('/');

function walk(dir, ext) {
  if (!existsSync(dir)) return [];
  const out = [];
  for (const entry of readdirSync(dir).sort()) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (entry === 'obj' || entry === 'bin' || entry === 'node_modules' || entry === 'out') continue;
      out.push(...walk(full, ext));
    } else if (entry.endsWith(ext)) {
      out.push(posix(full));
    }
  }
  return out;
}

// Devuelve el bloque balanceado que arranca en el open/close dado.
function balanced(text, from, open, close) {
  let depth = 0;
  for (let i = from; i < text.length; i++) {
    if (text[i] === open) depth++;
    else if (text[i] === close) {
      depth--;
      if (depth === 0) return text.slice(from, i + 1);
    }
  }
  return '';
}

const esc = (s) => String(s).replace(/\|/g, '\\|');
const code = (s) => '`' + s + '`';
const yesNo = (b) => (b ? 'si' : 'no');

// ===========================================================================
// 1. Colas del Service Bus  (infra/modules/servicebus.bicep)
// ===========================================================================

function parseQueues() {
  const src = read('infra/modules/servicebus.bicep');
  const start = src.indexOf('param queues array = [');
  const block = balanced(src, src.indexOf('[', start), '[', ']');
  const queues = [];
  const objRe = /\{/g;
  let m;
  while ((m = objRe.exec(block))) {
    const obj = balanced(block, m.index, '{', '}');
    if (!obj) continue;
    objRe.lastIndex = m.index + obj.length;
    const name = obj.match(/name:\s*'([^']+)'/);
    const sess = obj.match(/requiresSession:\s*(true|false)/);
    if (name) queues.push({ name: name[1], requiresSession: sess ? sess[1] === 'true' : false });
  }

  // Propiedades fijas del recurso, iguales para todas las colas.
  const props = {};
  for (const key of ['lockDuration', 'maxDeliveryCount', 'defaultMessageTimeToLive', 'deadLetteringOnMessageExpiration']) {
    const hit = src.match(new RegExp(key + ":\\s*'?([A-Za-z0-9]+)'?"));
    if (hit) props[key] = hit[1];
  }
  return { queues, props };
}

// ===========================================================================
// 2. Function Apps y sus app settings  (infra/main.bicep)
// ===========================================================================

// Objetos declarados en main.bicep (param X object = {...} / var X = {...}), para
// poder resolver valores como `schedules.customerGroupSync` al literal que traen
// por default.
function parseObjects(src) {
  const objects = {};
  const re = /(?:param\s+(\w+)\s+object\s*=|var\s+(\w+)\s*=)\s*\{/g;
  let m;
  while ((m = re.exec(src))) {
    const name = m[1] || m[2];
    const block = balanced(src, src.indexOf('{', m.index), '{', '}');
    if (!block) continue;
    const entries = {};
    for (const e of block.matchAll(/^\s*(\w+):\s*'([^']*)'\s*$/gm)) entries[e[1]] = e[2];
    objects[name] = entries;
  }
  return objects;
}

// `schedules.customerGroupSync` -> `0 0 23 * * *`, si se puede.
function resolveExpr(value, objects) {
  const literal = value.match(/^'(.*)'$/);
  if (literal) return literal[1];
  const dotted = value.match(/^(\w+)\.(\w+)$/);
  if (dotted && objects[dotted[1]] && objects[dotted[1]][dotted[2]] !== undefined) {
    return objects[dotted[1]][dotted[2]];
  }
  return null;
}

// fa-axxoncustomergroups-${environmentName} -> customergroups
// Es la clave con la que se cruza contra el nombre del proyecto; appKey no sirve
// (es un alias corto: custgroups).
const appKeyFromName = (functionAppName) =>
  functionAppName.replace(/^fa-axxon/, '').replace(/-\{env\}$/, '');

function parseApps() {
  const src = read('infra/main.bicep');
  const objects = parseObjects(src);
  const apps = [];
  const modRe = /module\s+(\w+)\s+'modules\/functionApp\.bicep'\s*=\s*(?:if\s*\(([^)]*)\)\s*)?\{/g;
  let m;
  while ((m = modRe.exec(src))) {
    const block = balanced(src, src.indexOf('{', m.index + m[0].length - 1), '{', '}');
    const grab = (key) => {
      const hit = block.match(new RegExp('^\\s*' + key + ':\\s*(.+)$', 'm'));
      return hit ? hit[1].trim() : null;
    };
    const settings = [];
    const asIdx = block.indexOf('appSettings:');
    if (asIdx !== -1) {
      const arrIdx = block.indexOf('[', asIdx);
      const arr = arrIdx === -1 ? '' : balanced(block, arrIdx, '[', ']');
      const itemRe = /\{\s*name:\s*'([^']+)'\s*,?\s*value:\s*([^}]+)\}/g;
      let s;
      while ((s = itemRe.exec(arr))) {
        const value = s[2].trim();
        settings.push({ name: s[1], value, resolved: resolveExpr(value, objects) });
      }
    }
    // concat(..., dataverseAuthSettings) agrega settings condicionales.
    const extras = [];
    if (/concat\([\s\S]*?dataverseAuthSettings/.test(block)) extras.push('dataverseAuthSettings');
    if (/concat\([\s\S]*?foAuthSettings/.test(block)) extras.push('foAuthSettings');

    const raw = (grab('functionAppName') || '').replace(/'/g, '');
    const name = raw.replace(/\$\{environmentName\}/, '{env}');
    apps.push({
      module: m[1],
      condition: (m[2] || '').trim() || null,
      name,
      key: appKeyFromName(name),
      appKey: (grab('appKey') || '').replace(/'/g, ''),
      maxInstances: grab('maximumInstanceCount'),
      needsServiceBus: grab('needsServiceBus') === 'true',
      publishesToServiceBus: grab('publishesToServiceBus') === 'true',
      settings,
      extras,
    });
  }
  return apps;
}

function parseBaseSettings() {
  const src = read('infra/modules/functionApp.bicep');
  const idx = src.indexOf('var baseAppSettings');
  if (idx === -1) return [];
  const arr = balanced(src, src.indexOf('[', idx), '[', ']');
  return [...arr.matchAll(/name:\s*'([^']+)'/g)].map((m) => m[1]);
}

// ===========================================================================
// 3. Functions y sus triggers  (src/integrations/**/*.cs)
// ===========================================================================

const APP_KEY_BY_PROJECT = (project) =>
  project.replace(/^Axxon/, '').replace(/\.Functions$/, '').toLowerCase();

function parseFunctions(apps) {
  // Mapa key -> { setting: valor } para resolver los %placeholders%.
  const settingsByApp = {};
  const appByKey = {};
  for (const app of apps) {
    appByKey[app.key] = app;
    settingsByApp[app.key] = Object.fromEntries(
      app.settings.map((s) => [s.name, s.resolved !== null ? s.resolved : null]).filter((e) => e[1])
    );
  }

  const found = [];
  for (const file of walk('src/integrations', '.cs')) {
    const parts = file.split('/');
    const project = parts[3];
    if (!project || !project.endsWith('.Functions')) continue;
    const appKey = APP_KEY_BY_PROJECT(project);
    const app = appByKey[appKey];
    const text = read(file);

    const fnRe = /\[Function\(\s*(?:nameof\(([^)]+)\)|"([^"]+)")\s*\)\]/g;
    let m;
    while ((m = fnRe.exec(text))) {
      const name = m[1] || m[2];
      const tail = text.slice(m.index, m.index + 900);
      const fn = { name, project, appKey, app: app ? app.name : appKey, file };

      const sb = tail.match(/\[ServiceBusTrigger\(\s*"([^"]+)"([\s\S]*?)\)\]/);
      const timer = tail.match(/\[TimerTrigger\(\s*"([^"]+)"/);
      const http = tail.match(/\[HttpTrigger\(\s*AuthorizationLevel\.(\w+)\s*,([\s\S]*?)\)\]/);

      if (sb) {
        fn.trigger = 'Service Bus';
        fn.target = resolvePlaceholder(sb[1], settingsByApp[appKey]);
        fn.sessions = /IsSessionsEnabled\s*=\s*true/.test(sb[2]);
      } else if (timer) {
        fn.trigger = 'Timer';
        fn.target = resolvePlaceholder(timer[1], settingsByApp[appKey], true);
      } else if (http) {
        fn.trigger = 'HTTP';
        const rest = http[2];
        const route = rest.match(/Route\s*=\s*"([^"]*)"/);
        const methods = [...rest.matchAll(/"([a-z]+)"/g)].map((x) => x[1].toUpperCase());
        fn.target = (methods.join('/') || 'ANY') + ' /api/' + (route ? route[1] : '');
        fn.auth = http[1];
      } else {
        fn.trigger = 'desconocido';
        fn.target = '';
      }
      found.push(fn);
    }
  }
  const order = { 'Service Bus': 0, Timer: 1, HTTP: 2, desconocido: 3 };
  return found.sort(
    (a, b) => order[a.trigger] - order[b.trigger] || a.appKey.localeCompare(b.appKey) || a.name.localeCompare(b.name)
  );
}

// %ServiceBusQueueName% -> el valor que le pone el Bicep, si lo sabemos.
function resolvePlaceholder(raw, settings, hierarchical = false) {
  const m = raw.match(/^%(.+)%$/);
  if (!m) return code(raw);
  const key = m[1];
  const flat = hierarchical ? key.replace(/:/g, '__') : key;
  const value = settings && (settings[key] || settings[flat]);
  return value ? code(value) + ' _(' + raw + ')_' : code(raw);
}

// ===========================================================================
// 4. Pipelines  (pipelines/*.yml)
// ===========================================================================

function parsePipelines() {
  const out = [];
  for (const file of readdirSync('pipelines').sort()) {
    if (!file.endsWith('.yml')) continue;
    const text = read(path.join('pipelines', file));
    const grab = (key) => {
      const hit = text.match(new RegExp('^\\s*' + key + ':\\s*(.+?)\\s*$', 'm'));
      return hit ? hit[1].replace(/'/g, '').replace(/\s*#.*$/, '') : null;
    };
    // paths.include del trigger
    const paths = [];
    const pIdx = text.indexOf('paths:');
    if (pIdx !== -1) {
      for (const line of text.slice(pIdx).split('\n').slice(2)) {
        const hit = line.match(/^\s+-\s+(\S+)\s*$/);
        if (!hit) break;
        paths.push(hit[1]);
      }
    }
    out.push({
      file: 'pipelines/' + file,
      appBaseName: grab('appBaseName'),
      projectPath: grab('projectPath'),
      testProjectPath: grab('testProjectPath'),
      deployToInte: grab('deployToInte'),
      deployToTest: grab('deployToTest'),
      inteAppName: grab('inteAppName'),
      testAppName: grab('testAppName'),
      manual: /^trigger:\s*none\s*$/m.test(text),
      paths,
    });
  }
  return out;
}

// ===========================================================================
// 5. Controles PCF  (ControlManifest.Input.xml)
// ===========================================================================

function parseControls() {
  const roots = ['src/webresources', 'src/integrations/contacts/AxxonContacts.PCF'];
  const out = [];
  for (const root of roots) {
    for (const file of walk(root, 'ControlManifest.Input.xml')) {
      const xml = read(file);
      const ctrl = xml.match(/<control[\s\S]*?>/);
      if (!ctrl) continue;
      const attr = (a) => {
        const hit = ctrl[0].match(new RegExp(a + '="([^"]*)"'));
        return hit ? hit[1] : '';
      };
      const props = [...xml.matchAll(/<property[^>]*name="([^"]+)"[^>]*of-type="([^"]+)"[^>]*usage="([^"]+)"/g)]
        .map((m) => m[1] + ' (' + m[3] + ', ' + m[2] + ')');
      out.push({
        namespace: attr('namespace'),
        constructor: attr('constructor'),
        version: attr('version'),
        dir: posix(path.dirname(path.dirname(file))),
        props,
      });
    }
  }
  return out.sort((a, b) => a.constructor.localeCompare(b.constructor));
}

// ===========================================================================
// Render
// ===========================================================================

const HEADER = [
  '<!-- wiki-meta',
  'sources: []',
  '-->',
  '<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->',
  '',
].join('\n');

function page(title, intro, body) {
  return HEADER + '# ' + title + '\n\n' + intro + '\n\n' + body.trimEnd() + '\n';
}

const GENERADO_NOTE =
  '> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está\n' +
  '> mal, el que está mal es el código — o el generador. No edites esta página.';

function renderFunciones(functions) {
  const rows = functions.map((f) => {
    const extra = f.trigger === 'Service Bus' ? ' — sessions: ' + yesNo(f.sessions)
      : f.trigger === 'HTTP' ? ' — auth: ' + f.auth
      : '';
    return '| ' + code(f.name) + ' | ' + code(f.app) + ' | ' + f.trigger + ' | ' + esc(f.target) + esc(extra) + ' |';
  });
  return page(
    'Inventario de funciones',
    GENERADO_NOTE + '\n\nLas ' + functions.length + ' Azure Functions de la plataforma, con el disparador que declara cada una.\nEntre paréntesis, el placeholder tal como está en el atributo; antes, el valor que le\nasigna `infra/main.bicep`.',
    ['| Function | App | Trigger | Cola / CRON / Ruta |', '|---|---|---|---|', ...rows].join('\n')
  );
}

function renderColas(queues, props, functions) {
  const consumers = {};
  const publishers = {};
  for (const f of functions) {
    if (f.trigger !== 'Service Bus') continue;
    const q = (f.target.match(/`([^`]+)`/) || [])[1];
    if (q) (consumers[q] = consumers[q] || []).push(f.name);
  }
  const rows = queues.map((q) => {
    const cons = (consumers[q.name] || []).map(code).join(', ') || '_ninguno en este repo_';
    return '| ' + code(q.name) + ' | ' + yesNo(q.requiresSession) + ' | ' + cons + ' |';
  });
  const propRows = Object.entries(props).map(([k, v]) => '| ' + code(k) + ' | ' + code(v) + ' |');
  return page(
    'Colas del Service Bus',
    GENERADO_NOTE + '\n\nLo que declara `infra/modules/servicebus.bicep` para `sb-chacomer-eip-{env}`, cruzado con\nlas funciones que las consumen. El **porqué** de cada decisión (sobre todo por qué sólo una\nlleva sessions) está en\n[Infraestructura › Queues del namespace](../plataforma/infraestructura.md#queues-del-namespace).',
    ['| Cola | Sessions | Consumidor |', '|---|---|---|', ...rows, '', '### Propiedades, iguales para todas', '', '| Propiedad | Valor |', '|---|---|', ...propRows].join('\n')
  );
}

function renderSettings(apps, base) {
  const parts = [];
  parts.push('### Base — las reciben todas las apps', '', '| Setting |', '|---|', ...base.map((s) => '| ' + code(s) + ' |'), '');
  for (const app of apps) {
    parts.push('### ' + code(app.name), '');
    const meta = [
      'appKey: ' + code(app.appKey),
      'instancias máx.: ' + code(app.maxInstances || '?'),
      'consume Service Bus: ' + yesNo(app.needsServiceBus),
      'publica en Service Bus: ' + yesNo(app.publishesToServiceBus),
    ];
    if (app.condition) meta.push('se despliega si: ' + code(app.condition));
    parts.push(meta.map((x) => '- ' + x).join('\n'), '');
    if (app.settings.length) {
      parts.push('| Setting | Valor en el template | Default |', '|---|---|---|');
      for (const s of app.settings) {
        const def = s.resolved !== null && s.resolved !== s.value.replace(/^'|'$/g, '') ? code(s.resolved) : '—';
        parts.push('| ' + code(s.name) + ' | ' + esc(code(s.value)) + ' | ' + esc(def) + ' |');
      }
    } else {
      parts.push('_Sin app settings propios._');
    }
    if (app.extras.length) {
      parts.push('', 'Suma ' + app.extras.map(code).join(' y ') + ', que el template emite sólo si el ambiente declara el client id correspondiente.');
    }
    parts.push('');
  }
  return page(
    'Application Settings por app',
    GENERADO_NOTE + '\n\nLo que declara `infra/main.bicep`. **El template declara la colección completa**: un\nsetting puesto a mano en el portal lo borra el próximo deployment.',
    parts.join('\n')
  );
}

function renderPipelines(pipes) {
  const integ = pipes.filter((p) => p.appBaseName);
  const infra = pipes.filter((p) => !p.appBaseName);
  const rows = integ.map((p) => {
    const inte = p.deployToInte === 'false' ? 'no' : p.inteAppName ? code(p.inteAppName) : 'si';
    const test = p.deployToTest === 'false' ? 'no' : p.testAppName ? code(p.testAppName) : 'si';
    return '| [' + path.basename(p.file) + '](../../../' + p.file + ') | ' + code(p.appBaseName) + ' | ' + inte + ' | ' + test + ' | ' + (p.testProjectPath ? code(p.testProjectPath) : '—') + ' |';
  });
  const infraRows = infra.map(
    (p) => '| [' + path.basename(p.file) + '](../../../' + p.file + ') | ' + (p.manual ? 'manual' : 'automático') + ' | ' + (p.paths.map(code).join(', ') || '—') + ' |'
  );
  const triggerRows = integ.map((p) => '| ' + code(p.appBaseName) + ' | ' + p.paths.map(code).join('<br>') + ' |');
  return page(
    'Matriz de pipelines',
    GENERADO_NOTE + '\n\nLo que declara `pipelines/*.yml`. El detalle de cómo funciona la promoción está en\n[Pipelines](../plataforma/pipelines.md).',
    [
      '## Integraciones',
      '',
      '| Pipeline | App base | INTE | TEST | Tests |',
      '|---|---|---|---|---|',
      ...rows,
      '',
      '## Qué dispara cada uno',
      '',
      '| App base | paths.include |',
      '|---|---|',
      ...triggerRows,
      '',
      '## Infraestructura',
      '',
      '| Pipeline | Disparo | paths.include |',
      '|---|---|---|',
      ...infraRows,
    ].join('\n')
  );
}

function renderControls(controls) {
  const rows = controls.map(
    (c) => '| ' + code(c.constructor) + ' | ' + code(c.namespace) + ' | ' + code(c.version) + ' | ' + (c.props.map(code).join(', ') || '—') + ' | ' + code(c.dir) + ' |'
  );
  return page(
    'Controles PCF',
    GENERADO_NOTE + '\n\nLo que declara cada `ControlManifest.Input.xml`. Qué hace cada control y dónde está puesto\nvive en [Web resources](../webresources.md).',
    ['| Control | Namespace | Versión | Propiedades | Carpeta |', '|---|---|---|---|---|', ...rows].join('\n')
  );
}

function renderIndex(files) {
  const rows = files.map((f) => '| [' + f.title + '](' + f.name + ') | ' + f.from + ' |');
  return page(
    'Páginas generadas',
    'Estas páginas **se generan desde el código** con [`generate.mjs`](../generate.mjs) y no se\neditan a mano: cualquier cambio se pierde en la próxima corrida. Por eso no pueden quedar\ndesactualizadas.\n\n```bash\nnode docs/wiki/generate.mjs\n```\n\nEl CI corre `--check`: si alguien cambia el código y no regenera, el check falla.',
    ['| Página | Se genera desde |', '|---|---|', ...rows].join('\n')
  );
}

// ===========================================================================
// Main
// ===========================================================================

const { queues, props } = parseQueues();
const apps = parseApps();
const baseSettings = parseBaseSettings();
const functions = parseFunctions(apps);
const pipes = parsePipelines();
const controls = parseControls();

const outputs = [
  { name: 'funciones.md', title: 'Inventario de funciones', from: '`src/integrations/**/*.cs` + `infra/main.bicep`', content: renderFunciones(functions) },
  { name: 'colas.md', title: 'Colas del Service Bus', from: '`infra/modules/servicebus.bicep`', content: renderColas(queues, props, functions) },
  { name: 'app-settings.md', title: 'Application Settings por app', from: '`infra/main.bicep` + `infra/modules/functionApp.bicep`', content: renderSettings(apps, baseSettings) },
  { name: 'pipelines.md', title: 'Matriz de pipelines', from: '`pipelines/*.yml`', content: renderPipelines(pipes) },
  { name: 'controles-pcf.md', title: 'Controles PCF', from: '`**/ControlManifest.Input.xml`', content: renderControls(controls) },
];
outputs.push({ name: 'README.md', title: 'Índice', from: '—', content: renderIndex(outputs) });

if (!existsSync(OUT)) mkdirSync(OUT, { recursive: true });

const drift = [];
for (const o of outputs) {
  const file = path.join(OUT, o.name);
  const current = existsSync(file) ? read(file) : null;
  if (current === o.content) continue;
  drift.push(o.name);
  if (!CHECK) writeFileSync(file, o.content);
}

// .order para Azure DevOps
const orderFile = path.join(OUT, '.order');
const order = outputs.filter((o) => o.name !== 'README.md').map((o) => o.name.replace(/\.md$/, '')).join('\n') + '\n';
if (!CHECK && read0(orderFile) !== order) writeFileSync(orderFile, order);
function read0(p) {
  return existsSync(p) ? read(p) : null;
}

console.log(
  'Fuentes: ' + functions.length + ' functions, ' + queues.length + ' colas, ' + apps.length + ' apps, ' +
  pipes.length + ' pipelines, ' + controls.length + ' controles PCF'
);

if (!drift.length) {
  console.log('Sin cambios: lo generado coincide con el codigo.');
  process.exit(0);
}

if (CHECK) {
  console.log('\nEstas paginas generadas quedaron desactualizadas:');
  drift.forEach((d) => console.log('  - ' + posix(path.join(OUT, d))));
  console.log('\nCorrer:  node docs/wiki/generate.mjs');
  process.exit(1);
}

console.log('Regeneradas: ' + drift.join(', '));
