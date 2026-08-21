#!/usr/bin/env node
// ---------------------------------------------------------------------------
// Chequeo de frescura de la wiki.
//
// Cada pagina de docs/wiki declara de que codigo depende en un bloque wiki-meta:
//
//   <!-- wiki-meta
//   sources:
//     - src/integrations/contacts/**
//   last_reviewed: 2026-08-21
//   -->
//
// Dos claves posibles:
//   sources:      cualquier cambio en esos archivos vuelve revisable la pagina.
//   sources_new:  solo el alta o baja de un archivo. Para paginas que son un
//                 inventario (indices, listados) y no se vuelven falsas porque
//                 alguien edite el cuerpo de una funcion que ya estaba.
// Sin ninguna de las dos (o con []), la pagina no se chequea por frescura.
//
// Este script contesta tres preguntas:
//   1. Falta el bloque wiki-meta en alguna pagina?
//   2. Hay paginas cuyos sources se tocaron despues de su last_reviewed?
//   3. Hay links relativos rotos?
//
// Modos:
//   node docs/wiki/check-freshness.mjs                     todo el historial (reporte)
//   node docs/wiki/check-freshness.mjs --diff origin/main  solo lo que cambia este branch
//   node docs/wiki/check-freshness.mjs --strict            exit 1 si hay hallazgos
//
// Sin dependencias: se corre con node, sin npm install.
// ---------------------------------------------------------------------------

import { execFileSync } from 'node:child_process';
import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

const WIKI_DIR = 'docs/wiki';
const BS = String.fromCharCode(92); // barra invertida, sin escribirla literal

const args = process.argv.slice(2);
const STRICT = args.includes('--strict');
// Falla solo por problemas estructurales (links rotos, falta de wiki-meta) y deja
// la frescura en modo aviso: no queremos trabar un PR por una fecha.
const STRICT_LINKS = args.includes('--strict-links');
const diffIdx = args.indexOf('--diff');
const DIFF_BASE = diffIdx !== -1 ? args[diffIdx + 1] : null;

const git = (...a) => execFileSync('git', a, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const posix = (p) => p.split(path.sep).join('/');

// --- helpers ---------------------------------------------------------------

const SPECIAL = '.+^${}()|[]/' + BS;

function globToRegExp(glob) {
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*') {
      if (glob[i + 1] === '*') {
        // ** = cualquier cosa, cruzando barras. Consume el / que le siga.
        i++;
        if (glob[i + 1] === '/') i++;
        re += '.*';
      } else {
        re += '[^/]*';
      }
    } else if (c === '?') {
      re += '[^/]';
    } else if (SPECIAL.includes(c)) {
      re += BS + c;
    } else {
      re += c;
    }
  }
  return new RegExp('^' + re + '$');
}

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...walk(full));
    else if (entry.endsWith('.md')) out.push(posix(full));
  }
  return out;
}

const META_RE = new RegExp('<!--[' + BS + 's]*wiki-meta([' + BS + 's' + BS + 'S]*?)-->');

function parseMeta(file) {
  const text = readFileSync(file, 'utf8');
  const block = text.match(META_RE);
  if (!block) return { text, meta: null };
  const sources = [];
  const sourcesNew = [];
  let list = null;
  let lastReviewed = null;
  for (const raw of block[1].split('\n')) {
    const line = raw.trim();
    if (line.startsWith('sources_new:')) {
      list = line.includes('[]') ? null : sourcesNew;
      continue;
    }
    if (line.startsWith('sources:')) {
      list = line.includes('[]') ? null : sources;
      continue;
    }
    if (line.startsWith('last_reviewed:')) {
      list = null;
      lastReviewed = line.slice('last_reviewed:'.length).trim();
      continue;
    }
    if (list && line.startsWith('- ')) list.push(line.slice(2).trim());
    else if (line && !line.startsWith('- ')) list = null;
  }
  return { text, meta: { sources, sourcesNew, lastReviewed } };
}

// Historial como [{ date, subject, files: [{ status, path }] }], mas nuevo primero.
// status es el de --name-status: A (agregado), M (modificado), D (borrado).
function commitsFrom(...logArgs) {
  const raw = git('log', '--format=@%cI\t%s', '--name-status', '--no-renames', ...logArgs);
  const commits = [];
  let current = null;
  for (const line of raw.split('\n')) {
    if (line.startsWith('@')) {
      const [iso, ...rest] = line.slice(1).split('\t');
      current = { date: iso.slice(0, 10), subject: rest.join('\t'), files: [] };
      commits.push(current);
    } else if (line.trim() && current) {
      const [status, filePath] = line.split('\t');
      if (filePath) current.files.push({ status: status.trim(), path: filePath.trim() });
    }
  }
  return commits;
}

// --- carga -----------------------------------------------------------------

const pages = walk(WIKI_DIR).map((file) => ({ file, ...parseMeta(file) }));

const noMeta = pages.filter((p) => !p.meta).map((p) => p.file);
const withMeta = pages.filter((p) => p.meta);
for (const p of withMeta) {
  p.matchers = p.meta.sources.map(globToRegExp);
  p.matchersNew = p.meta.sourcesNew.map(globToRegExp);
}

// sources: cualquier cambio cuenta. sources_new: solo alta o baja de archivo,
// para las paginas que son un inventario y no se vuelven falsas por un edit.
const touches = (page, f) =>
  page.matchers.some((re) => re.test(f.path)) ||
  (f.status !== 'M' && page.matchersNew.some((re) => re.test(f.path)));

// --- 1. paginas rancias ----------------------------------------------------

const scope = DIFF_BASE ? commitsFrom(DIFF_BASE + '..HEAD') : commitsFrom('-n', '1000');

const stale = [];
for (const page of withMeta) {
  if (!page.meta.sources.length && !page.meta.sourcesNew.length) continue;
  // Un commit que ademas toca la pagina ya la actualizo: no cuenta como deuda.
  const hits = scope.filter(
    (c) => c.files.some((f) => touches(page, f)) && !c.files.some((f) => f.path === page.file)
  );
  if (!hits.length) continue;
  const newest = hits[0];
  if (DIFF_BASE || newest.date > (page.meta.lastReviewed || '')) {
    stale.push({ page: page.file, since: page.meta.lastReviewed, commits: hits.slice(0, 5) });
  }
}

// --- 2. links relativos rotos ---------------------------------------------

// Equivale a  /\]\(([^)\s]+)/g  — armado asi para no escribir barras invertidas.
const LINK_RE = new RegExp(BS + ']' + BS + '(([^)' + BS + 's]+)', 'g');

// Ademas de la wiki, los archivos que apuntan a ella: si se mueve una pagina,
// estos son los que quedan colgados.
const EXTRA = ['README.md', 'CLAUDE.md', 'infra/README.md', 'docs/contracts/README.md']
  .concat(
    readdirSync('src/integrations')
      .flatMap((dom) => {
        const dir = path.join('src/integrations', dom);
        if (!statSync(dir).isDirectory()) return [];
        return readdirSync(dir).map((proj) => path.join(dir, proj, 'readme.md'));
      })
  )
  .filter((f) => existsSync(f))
  .map((f) => ({ file: posix(f), text: readFileSync(f, 'utf8') }));

const broken = [];
for (const page of pages.concat(EXTRA)) {
  for (const m of page.text.matchAll(LINK_RE)) {
    const target = m[1];
    if (/^(https?:|mailto:|#)/.test(target)) continue;
    const [rel] = target.split('#');
    if (!rel) continue;
    if (!existsSync(path.resolve(path.dirname(page.file), rel))) {
      broken.push({ page: page.file, target });
    }
  }
}

// --- reporte ---------------------------------------------------------------

const bullet = (s) => '  - ' + s;
let findings = 0;

console.log('Wiki: ' + pages.length + ' paginas' + (DIFF_BASE ? ' | diff contra ' + DIFF_BASE : '') + '\n');

if (noMeta.length) {
  findings += noMeta.length;
  console.log('Paginas sin bloque wiki-meta:');
  noMeta.forEach((f) => console.log(bullet(f)));
  console.log('  Agregar el bloque arriba de todo, declarando sources y last_reviewed.\n');
}

if (stale.length) {
  findings += stale.length;
  console.log(DIFF_BASE
    ? 'Este branch toca codigo del que dependen estas paginas, y no las actualiza:'
    : 'Paginas cuyo codigo cambio despues del ultimo last_reviewed:');
  for (const s of stale) {
    console.log('  ' + s.page + '  (last_reviewed: ' + (s.since || 'sin fecha') + ')');
    s.commits.forEach((c) => console.log('      ' + c.date + '  ' + c.subject));
  }
  console.log('  Revisar la pagina y actualizar last_reviewed en el mismo PR.\n');
}

if (broken.length) {
  findings += broken.length;
  console.log('Links relativos rotos:');
  broken.forEach((b) => console.log(bullet(b.page + '  ->  ' + b.target)));
  console.log('');
}

console.log(findings ? findings + ' hallazgo(s).' : 'Sin hallazgos: la wiki esta al dia y los links resuelven.');

// Las anclas (#seccion) no se validan a proposito: el slug de GitHub y el de
// Azure DevOps difieren lo suficiente como para generar falsos positivos.

const structural = noMeta.length + broken.length;
if (STRICT && findings) process.exit(1);
if (STRICT_LINKS && structural) process.exit(1);
process.exit(0);
