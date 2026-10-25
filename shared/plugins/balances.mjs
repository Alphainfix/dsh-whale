// Vibe Windows companion: original Whale balance protocol, behind DSH browser auth.
import { spawn } from 'node:child_process';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {registerLogin} from './chatgpt-login.mjs';

export const name = 'whale-balances';
export const inject = ['connection', 'credentials', 'authorization'];
const failure = (label, error, kind='prepaid') => ({ok:false,label,kind,error});
async function json(url, key, fetcher=fetch) {
  const response=await fetcher(url,{headers:{Authorization:`Bearer ${key}`},signal:AbortSignal.timeout(12000)});
  if(!response.ok)throw new Error(`HTTP ${response.status}`);
  return response.json();
}
function numeric(value) { const n=typeof value==='number'?value:typeof value==='string'&&value.trim()?Number(value):NaN; if(!Number.isFinite(n))throw new Error('余额响应格式不兼容');return n; }
export async function deepseek(credentials,fetcher=fetch) {
  const key=await credentials.resolve('DEEPSEEK_API_KEY');
  if(!key?.value)return failure('DeepSeek','请在 DSH 模型设置中配置 DeepSeek API Key');
  const data=await json('https://api.deepseek.com/user/balance',key.value,fetcher);
  const balance=data.balance_infos?.find(b=>b.currency==='CNY')??data.balance_infos?.find(b=>b.currency==='USD');
  if(!balance)throw new Error('余额响应格式不兼容');
  return {ok:true,label:'DeepSeek',kind:'prepaid',currency:balance.currency,remaining:numeric(balance.total_balance)};
}
export async function openrouter(credentials,fetcher=fetch) {
  const management=await credentials.resolve('OPENROUTER_MANAGEMENT_KEY');
  const key=management?.value?management:await credentials.resolve('OPENROUTER_API_KEY');
  if(!key?.value)return failure('OpenRouter','请在 DSH 模型设置中配置 OpenRouter API Key');
  try {
    const {data}=await json('https://openrouter.ai/api/v1/credits',key.value,fetcher);
    return {ok:true,label:'OpenRouter',kind:'prepaid',currency:'USD',remaining:numeric(data.total_credits)-numeric(data.total_usage)};
  } catch(error) {
    if(!['HTTP 401','HTTP 403'].includes(error.message)||management?.value)throw error;
    const {data}=await json('https://openrouter.ai/api/v1/key',key.value,fetcher);
    if(data.limit_remaining==null)return failure('OpenRouter','此 Key 无独立限额；账户余额需要 Management Key');
    return {ok:true,label:'OpenRouter Key 额度',kind:'quota',currency:'USD',remaining:numeric(data.limit_remaining),limit:data.limit==null?undefined:numeric(data.limit)};
  }
}
async function codexExecutable() {
  const root=path.join(process.env.LOCALAPPDATA||'', 'OpenAI/Codex/bin');
  const versions=await readdir(root,{withFileTypes:true}).catch(()=>[]);
  for(const version of versions.reverse())if(version.isDirectory()) {
    const folder=path.join(root,version.name);
    if((await readdir(folder)).includes('codex.exe'))return path.join(folder,'codex.exe');
  }
  throw new Error('查看 ChatGPT 用量需要本机 Codex CLI；不影响 DSH 调用模型');
}
// Ephemeral app-server uses only the DSH grant, never the user's Codex profile.
// Secrets travel over stdin; stderr, credentials and account identity are never returned.
export async function readRateLimits(grant, executable=codexExecutable) {
  const exe=await executable();
  const home=await mkdtemp(path.join(os.tmpdir(),'dsh-whale-quota-'));
  let child;
  try {
    const env={...process.env,CODEX_HOME:home};delete env.OPENAI_API_KEY;delete env.CODEX_API_KEY;
    child=spawn(exe,['app-server'],{env,windowsHide:true,stdio:['pipe','pipe','ignore']});
    const pending=new Map();let serial=0,buffer='';
    const rejectAll=()=>{for(const {reject} of pending.values())reject(new Error('Codex 用量服务已退出'));pending.clear();};
    child.on('error',rejectAll);child.on('exit',rejectAll);
    child.stdout.setEncoding('utf8');child.stdout.on('data',chunk=>{
      buffer+=chunk;if(buffer.length>1048576){rejectAll();child.kill();return;}
      let end;while((end=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,end);buffer=buffer.slice(end+1);let message;try{message=JSON.parse(line);}catch{continue;}
        const request=pending.get(message.id);
        if(request){pending.delete(message.id);message.error?request.reject(new Error('ChatGPT 用量接口拒绝请求，请检查登录是否有效')):request.resolve(message.result);}
        else if(message.id!==undefined&&message.method)child.stdin.write(JSON.stringify({id:message.id,error:{code:-32000,message:'Refresh the grant through DSH login.'}})+'\n');
      }
    });
    const rpc=(method,params)=>new Promise((resolve,reject)=>{
      const id=++serial;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('ChatGPT 用量请求超时'));},18000);
      pending.set(id,{resolve:value=>{clearTimeout(timer);resolve(value);},reject:error=>{clearTimeout(timer);reject(error);}});
      child.stdin.write(JSON.stringify({id,method,params})+'\n',error=>{if(error){pending.get(id)?.reject(new Error('Codex 用量服务连接失败'));pending.delete(id);}});
    });
    await rpc('initialize',{clientInfo:{name:'dsh_whale',version:'1.0.0'},capabilities:{experimentalApi:true}});
    child.stdin.write(JSON.stringify({method:'initialized'})+'\n');
    await rpc('account/login/start',{type:'chatgptAuthTokens',accessToken:grant.access,chatgptAccountId:grant.accountId});
    return await rpc('account/rateLimits/read',{});
  } finally {
    if(child){child.stdin.end();if(child.pid&&child.exitCode===null)await new Promise(resolve=>{const timer=setTimeout(()=>{child.kill();resolve();},3000);child.once('close',()=>{clearTimeout(timer);resolve();});});}
    if(path.dirname(path.resolve(home))!==path.resolve(os.tmpdir())||!path.basename(home).startsWith('dsh-whale-quota-'))throw new Error('Unexpected temporary directory');
    // Windows scanners can briefly retain SQLite/log files after process exit.
    // Cleanup must not turn a successful quota response into a network error.
    await rm(home,{recursive:true,force:true,maxRetries:10,retryDelay:200}).catch(error=>{if(!['EBUSY','EPERM','ENOTEMPTY'].includes(error.code))throw error;});
  }
}
export function quotaRows(result) {
  const buckets=result.rateLimitsByLimitId;
  const limits=buckets?.codex??result.rateLimits;
  const rows={};
  for(const [key,id] of [['primary','codex'],['secondary','codex-secondary']]) {
    const window=limits?.[key];if(!window)continue;
    const mins=numeric(window.windowDurationMins),hours=mins/60;
    if(mins<=0)continue;
    const duration=mins%10080===0?`${mins/10080}周`:hours%24===0?`${hours/24}天`:`${hours}h`;
    rows[id]={ok:true,label:`ChatGPT ${duration} 余量`,kind:'quota',currency:'%',remaining:Math.min(100,Math.max(0,100-numeric(window.usedPercent))),limit:100,resetsAt:window.resetsAt};
  }
  if(!Object.keys(rows).length)rows.codex=failure('ChatGPT','当前账户没有返回可显示的订阅用量','quota');
  return rows;
}
export async function chatgpt(credentials,read=readRateLimits) {
  const record=await credentials.readRecord('llm-pi-ai/openai-codex');
  const grant=record?.kind==='grant'?record.payload:undefined;
  if(!grant?.access||!grant?.accountId)return {codex:failure('ChatGPT','请点击小鲸鱼设置中的「登录 ChatGPT」','quota')};
  if(grant.expires&&grant.expires<=Date.now())return {codex:failure('ChatGPT','授权需刷新：先在 DSH 使用 GPT 或重新登录','quota')};
  return quotaRows(await read(grant));
}
export function apply(ctx) {
  let stopped=false,busy=false;
  let providers={deepseek:failure('DeepSeek','正在读取'),openrouter:failure('OpenRouter','正在读取'),codex:failure('ChatGPT','正在读取','quota')};
  async function refresh(){if(stopped||busy)return;busy=true;try{
    const safe=async(label,fn,kind)=>{try{return await fn();}catch(error){const msg=/^(HTTP \d{3}|余额响应格式不兼容|ChatGPT |Codex |查看 ChatGPT )/.test(error.message)?error.message:'网络连接失败，稍后重试';return failure(label,msg,kind);}};
    const [ds,or,cg]=await Promise.all([safe('DeepSeek',()=>deepseek(ctx.credentials)),safe('OpenRouter',()=>openrouter(ctx.credentials)),safe('ChatGPT',()=>chatgpt(ctx.credentials),'quota')]);
    if(!stopped)providers={deepseek:ds,openrouter:or,...(cg.codex?cg:{codex:cg})};
  }finally{busy=false;}}
  ctx.effect(()=>ctx.connection.fetch.register({path:'/api/model-balance',methods:['GET'],requestBody:'buffered',fetch:()=>Response.json({ok:true,providers},{headers:{'Cache-Control':'no-store'}})}),'whale balance API');
  registerLogin(ctx,()=>void refresh());
  ctx.effect(()=>{void refresh();const timer=setInterval(()=>void refresh(),60000);return()=>{stopped=true;clearInterval(timer);};},'whale balance refresh');
}
