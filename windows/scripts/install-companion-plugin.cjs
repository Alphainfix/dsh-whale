const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const YAML=require(path.join(process.argv[2],'yaml'));
const file=path.join(process.env.DSH_HOME||path.join(os.homedir(),'.dsh'),'profiles/web/cordis.patch.yml');
const before=fs.readFileSync(file,'utf8');
const doc=YAML.parseDocument(before,{customTags:[{tag:'tag:yaml.org,2002:js',resolve:value=>value}]});
if(doc.errors.length||!YAML.isSeq(doc.contents))throw Error('Invalid profile; unchanged.');
const id='vibe-whale-balances';
const pluginRoot=fs.existsSync(path.resolve(__dirname,'../plugins/balances.mjs'))?path.resolve(__dirname,'../plugins'):path.resolve(__dirname,'../../shared/plugins');
const plugin=path.join(pluginRoot,'balances.mjs').replaceAll('\\','/');
let found=false;
for(const entry of doc.contents.items){const list=entry.get?.('insert');if(YAML.isSeq(list))for(const row of list.items)if(row.get?.('id')===id){row.set('name',plugin);found=true;}}
if(!found)doc.contents.add(doc.createNode({insert:[{id,name:plugin}]}));
const healthId='vibe-whale-model-health';
const healthPath=path.join(pluginRoot,'model-health.mjs').replaceAll('\\','/');
let healthFound=false;
for(const entry of doc.contents.items){const list=entry.get?.('insert');if(YAML.isSeq(list))for(const row of list.items)if(row.get?.('id')===healthId){row.set('name',healthPath);healthFound=true;}}
if(!healthFound)doc.contents.add(doc.createNode({insert:[{id:healthId,name:healthPath}]}));
if(fs.readFileSync(file,'utf8')!==before)throw Error('Concurrent change; retry.');
fs.copyFileSync(file,file+'.whale-'+Date.now()+'.bak',fs.constants.COPYFILE_EXCL);
fs.writeFileSync(file,doc.toString());console.log('Installed Vibe balance and native ChatGPT login bridge.');
