import {randomUUID} from 'node:crypto';
const key='llm-pi-ai/openai-codex';
const base='/api/whale/chatgpt';
export function registerLogin(ctx,onAuthorized) {
  let current={status:'idle',notices:[]},pending,controller,timeout;
  const route=(url,methods,handler)=>ctx.effect(()=>ctx.connection.fetch.register({path:url,methods,requestBody:'buffered',fetch:handler}),'whale ChatGPT login');
  route(base,['GET'],()=>new Response(page,{headers:{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','Content-Security-Policy':"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'"}}));
  route(base+'/state',['GET'],async()=>Response.json({...current,configured:!!(await ctx.credentials.readRecord(key)),available:!!ctx.authorization.describe(key)},{headers:{'Cache-Control':'no-store'}}));
  route(base+'/action',['POST'],async request=>{
    if(request.headers.get('X-Whale-Action')!=='1')return new Response('Forbidden',{status:403});
    let body;try{body=await request.json();}catch{return new Response('Invalid JSON',{status:400});}
    if(body.action==='start') {
      if(current.status==='running')return new Response('Login already running',{status:409});
      if(!ctx.authorization.describe(key))return new Response('Native OAuth provider unavailable',{status:503});
      controller=new AbortController();current={status:'running',notices:[]};
      timeout=setTimeout(()=>controller.abort(),10*60*1000);
      void ctx.authorization.begin({key,method:'oauth',signal:controller.signal,interaction:{
        notify(notice){current.notices.push(notice);current.notices=current.notices.slice(-8);},
        prompt(prompt){return new Promise((resolve,reject)=>{
          const id=randomUUID();const {signal,...view}=prompt;current.prompt={...view,id};
          const abort=()=>{if(current.prompt?.id===id)delete current.prompt;if(pending?.id===id)pending=undefined;reject(new Error('Prompt withdrawn'));};
          if(signal?.aborted||controller.signal.aborted){abort();return;}
          signal?.addEventListener('abort',abort,{once:true});controller.signal.addEventListener('abort',abort,{once:true});
          pending={id,resolve(value){signal?.removeEventListener('abort',abort);controller.signal.removeEventListener('abort',abort);resolve(value);}};
        });}
      }}).then(result=>{current.status=result.status;if(result.status==='authorized')onAuthorized();}).catch(error=>{
        current.status=controller.signal.aborted?'cancelled':'failed';
        // OAuth errors may contain response bodies or codes. Return only a safe category.
        current.error=error.code==='ALREADY_IN_FLIGHT'?'已有另一个登录流程，请先完成或取消它。':'登录未完成，请重试；可选择 Device code login。';
      }).finally(()=>{clearTimeout(timeout);pending=undefined;delete current.prompt;current.notices=[];});
    } else if(body.action==='answer') {
      if(!pending||body.id!==pending.id||typeof body.value!=='string'||body.value.length>8192)return new Response('Prompt expired',{status:409});
      if(current.prompt.kind==='select'&&!current.prompt.options.some(o=>o.id===body.value))return new Response('Invalid choice',{status:400});
      const answer=pending;pending=undefined;delete current.prompt;answer.resolve(body.value);
    } else if(body.action==='cancel'){controller?.abort();}
    else return new Response('Unknown action',{status:400});
    return Response.json({ok:true});
  });
  ctx.effect(()=>()=>{clearTimeout(timeout);controller?.abort();},'whale login cleanup');
}
const page=`<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>登录 ChatGPT · DSH 小鲸鱼</title>
<style>[hidden]{display:none!important}body{font:15px/1.6 system-ui;margin:60px auto;padding:0 24px;max-width:660px;background:#f7f8fa;color:#20242b}main{background:white;border:1px solid #e3e5e8;border-radius:16px;padding:32px}h1{font-size:24px;margin-top:0}p{color:#626976}button,a.button{border:0;border-radius:8px;padding:11px 18px;background:#1571e8;color:white;cursor:pointer;display:inline-block;margin:8px 8px 8px 0;text-decoration:none}button:disabled{opacity:.45;cursor:default}#cancel{background:#69717d}input,select{box-sizing:border-box;width:100%;padding:12px;border:1px solid #ccd2da;border-radius:6px;font:inherit}#state{font-weight:600}#notice{white-space:pre-wrap;overflow-wrap:anywhere}.muted{font-size:13px}code{user-select:all}#error{color:#ac3030}</style>
<main><h1>登录 ChatGPT</h1><p>让 DSH 使用你 ChatGPT 账号可用的 GPT 模型与 Codex 订阅额度。授权由 DSH 原生提供方处理，登录信息保存在 DSH 中。</p><div id="state">正在连接 DSH…</div><button id="start">登录 ChatGPT</button><button id="cancel" hidden>取消登录</button><div id="notice"></div><form id="prompt" hidden><label id="question"></label><div id="field"></div><button>继续</button></form><div id="error"></div><p class="muted">成功后，回到 DSH 的模型选择器选择 ChatGPT (OAuth) 下的 GPT 模型。此登录不会给 OpenAI API 余额充值。</p><a href="/" class="button">返回 DSH</a><p id="models" class="muted"></p></main>
<script>
const $=id=>document.getElementById(id);let promptId,signature;
async function action(body){$('error').textContent='';try{const r=await fetch('${base}/action',{method:'POST',headers:{'Content-Type':'application/json','X-Whale-Action':'1'},body:JSON.stringify(body)});if(!r.ok)throw Error('操作未完成（HTTP '+r.status+'），请刷新页面重试。');await update();}catch(e){$('error').textContent=e.message;}}
$('start').onclick=()=>action({action:'start'});$('cancel').onclick=()=>action({action:'cancel'});
$('prompt').onsubmit=e=>{e.preventDefault();const value=$('answer').value;action({action:'answer',id:promptId,value});$('answer').value='';};
async function update(){try{const r=await fetch('${base}/state',{cache:'no-store'});if(!r.ok)throw Error('请先从小鲸鱼打开 DSH 完成本机认证，再刷新此页。');const s=await r.json();
$('state').textContent=s.status==='running'?'请按下方提示继续':s.status==='authorized'||s.configured?'已登录 ChatGPT':s.status==='failed'?'登录失败':s.status==='cancelled'?'已取消':'尚未登录';$('start').disabled=s.status==='running'||!s.available;$('start').textContent=s.configured?'重新登录 ChatGPT':'登录 ChatGPT';$('cancel').hidden=s.status!=='running';
if(s.error)$('error').textContent=s.error;
const sig=JSON.stringify(s.notices);if(sig!==signature){signature=sig;$('notice').replaceChildren();for(const n of s.notices){const p=document.createElement('p');p.textContent=n.message;$('notice').append(p);if(n.code){const c=document.createElement('code');c.textContent=n.code;$('notice').append(c);}if(n.url){try{const u=new URL(n.url);if(u.protocol==='https:'&&['auth.openai.com','chatgpt.com'].includes(u.hostname)){const a=document.createElement('a');a.href=u.href;a.target='_blank';a.rel='noopener noreferrer';a.className='button';a.textContent='打开 OpenAI 授权页面';$('notice').append(a);}}catch{}}}}
$('prompt').hidden=!s.prompt;if(s.prompt&&s.prompt.id!==promptId){promptId=s.prompt.id;$('question').textContent=s.prompt.message;$('field').replaceChildren();const input=document.createElement(s.prompt.kind==='select'?'select':'input');input.id='answer';if(s.prompt.kind==='select'){for(const o of s.prompt.options){const opt=document.createElement('option');opt.value=o.id;opt.textContent=o.label;input.append(opt);}}else{input.type=s.prompt.kind==='secret'?'password':'text';input.autocomplete='off';input.placeholder=s.prompt.placeholder||'';}$('field').append(input);}if(!s.prompt)promptId=null;
}catch(e){$('error').textContent=e.message;}}update();setInterval(update,1200);
fetch('/api/whale/models').then(r=>r.json()).then(data=>{const provider=data.providers.find(p=>p.id==='openai-codex');$('models').textContent=provider?'模型提供方：'+provider.name+'（openai-codex）。可用模型：'+data.models.map(m=>m.id).join('、'):'ChatGPT 路由尚未配置，请运行安装目录中的 setup-windows.ps1。';}).catch(()=>{});
</script></html>`;
