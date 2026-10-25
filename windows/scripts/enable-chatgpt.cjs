// Enable DSH's native subscription OAuth route; no credentials are copied.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const moduleRoot = process.argv[2];
if (!moduleRoot) throw new Error('Pass the DSH node_modules directory.');
const YAML = require(path.join(moduleRoot, 'yaml'));
const file = path.join(process.env.DSH_HOME || path.join(os.homedir(), '.dsh'), 'profiles/web/cordis.patch.yml');
const before = fs.readFileSync(file, 'utf8');
const document = YAML.parseDocument(before, {customTags: [{tag: 'tag:yaml.org,2002:js', resolve: value => value}]});
if (document.errors.length) throw new Error('Invalid profile YAML; left unchanged.');
if (!YAML.isSeq(document.contents)) throw new Error('Expected patch array; left unchanged.');
let row = document.contents.items.find(item => item.get?.('id') === 'llm-pi-ai');
if (!row) { row = document.createNode({id:'llm-pi-ai', name:'@deepseek-ai/dsh-llm-pi-ai',config:{providers:{}}}); document.contents.add(row); }
const existing = row.getIn(['config','providers','openai-codex']);
if (existing) {
  if(existing.has?.('apiKeyEnv') || existing.has?.('baseURL')) throw new Error('Existing Codex overrides need review; left unchanged.');
  if (!existing.get('displayName')) existing.set('displayName','ChatGPT (OAuth)');
} else {
  row.setIn(['config','providers','openai-codex'],document.createNode({displayName:'ChatGPT (OAuth)'}));
}
if (document.toString() !== before) {
  if (fs.readFileSync(file,'utf8') !== before) throw new Error('Profile changed concurrently; retry.');
  const backup = file + '.whale-' + Date.now() + '.bak';
  fs.copyFileSync(file, backup, fs.constants.COPYFILE_EXCL);
  fs.writeFileSync(file, document.toString());
  console.log('Enabled native ChatGPT OAuth provider. Existing settings preserved; backup saved.');
}
