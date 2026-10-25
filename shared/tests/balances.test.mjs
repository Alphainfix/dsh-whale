import {test} from 'node:test';
import assert from 'node:assert/strict';
import {deepseek,openrouter,chatgpt,quotaRows} from '../plugins/balances.mjs';
import {registerLogin} from '../plugins/chatgpt-login.mjs';
const credentials={resolve:async()=>({value:'test-only-key'}),readRecord:async()=>undefined};
test('DeepSeek decimal strings are converted, absent data is rejected',async()=>{
 assert.equal((await deepseek(credentials,async()=>Response.json({balance_infos:[{currency:'CNY',total_balance:'14.06'}]}))).remaining,14.06);
 await assert.rejects(()=>deepseek(credentials,async()=>Response.json({balance_infos:[]})));
});
test('OpenRouter ordinary key fallback is labelled as key allowance',async()=>{
 const creds={resolve:async ref=>ref==='OPENROUTER_API_KEY'?{value:'test-key'}:undefined};
 const result=await openrouter(creds,async url=>url.endsWith('/credits')?new Response('',{status:403}):Response.json({data:{limit_remaining:4,limit:10}}));
 assert.equal(result.label,'OpenRouter Key 额度');assert.equal(result.remaining,4);
 const unlimited=await openrouter(creds,async url=>url.endsWith('/credits')?new Response('',{status:403}):Response.json({data:{limit_remaining:null}}));
 assert.equal(unlimited.ok,false);assert.equal(unlimited.remaining,undefined);
});
test('ChatGPT missing and expired DSH grants never use another Codex login',async()=>{
 const unexpected=()=>{throw Error('must not launch');};
 assert.equal((await chatgpt(credentials,unexpected)).codex.ok,false);
 assert.equal((await chatgpt({readRecord:async()=>({kind:'grant',payload:{access:'test',accountId:'test',expires:1}})},unexpected)).codex.ok,false);
});
test('Quota window names and remaining values come from actual server windows',()=>{
 const rows=quotaRows({rateLimits:{primary:{windowDurationMins:300,usedPercent:28,resetsAt:42},secondary:{windowDurationMins:10080,usedPercent:110}}});
 assert.equal(rows.codex.label,'ChatGPT 5h 余量');assert.equal(rows.codex.remaining,72);assert.equal(rows['codex-secondary'].remaining,0);
 assert.equal(rows['codex-secondary'].label,'ChatGPT 1周 余量');
});
test('Login bridge requires authenticated-transport registration and action header; stale answers rejected',async()=>{
 const routes=new Map(),disposers=[];let committed=0;
 const ctx={connection:{fetch:{register:route=>{routes.set(route.path,route);return()=>{};}}},effect:fn=>disposers.push(fn()),credentials:{readRecord:async()=>undefined},authorization:{describe:()=>({}),begin:async({interaction})=>{const answer=await interaction.prompt({kind:'select',message:'Method',options:[{id:'browser',label:'Browser'}]});assert.equal(answer,'browser');return {status:'authorized'};}}};
 registerLogin(ctx,()=>committed++);
 const act=routes.get('/api/whale/chatgpt/action').fetch,state=routes.get('/api/whale/chatgpt/state').fetch;
 const req=(body,header=true)=>new Request('http://localhost/api/whale/chatgpt/action',{method:'POST',headers:header?{'X-Whale-Action':'1'}:{},body:JSON.stringify(body)});
 assert.equal((await act(req({action:'start'},false))).status,403);
 assert.equal((await act(req({action:'start'}))).status,200);
 const view=await (await state()).json();assert.equal(view.status,'running');
 assert.equal((await act(req({action:'answer',id:'stale',value:'browser'}))).status,409);
 assert.equal((await act(req({action:'answer',id:view.prompt.id,value:'invented'}))).status,400);
 assert.equal((await act(req({action:'answer',id:view.prompt.id,value:'browser'}))).status,200);
 await new Promise(resolve=>setImmediate(resolve));assert.equal(committed,1);
 for(const dispose of disposers)dispose?.();
});
