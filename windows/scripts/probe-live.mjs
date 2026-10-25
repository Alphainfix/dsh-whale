import fs from 'node:fs';import os from 'node:os';import path from 'node:path';
const origin='http://127.0.0.1:3080';
const log=fs.readFileSync(path.join(os.homedir(),'.dsh/logs/dsh-web.out.log'),'utf8');
const matches=[...log.matchAll(/http:\/\/(?:127\.0\.0\.1|localhost):3080\/\?token=[A-Za-z0-9._~%+/-]+={0,2}/g)];
if(!matches.length)throw Error('No local DSH authentication URL.');
const login=await fetch(matches.at(-1)[0],{redirect:'manual'});
const cookie=login.headers.getSetCookie().map(x=>x.split(';')[0]).join('; ');
for(const route of ['/api/model-balance','/api/whale/chatgpt/state']){
 const anonymous=await fetch(origin+route,{signal:AbortSignal.timeout(5000)});
 if(anonymous.status!==401&&anonymous.status!==403)throw Error('Endpoint unexpectedly permits unauthenticated access: '+route);
 const response=await fetch(origin+route,{headers:{Cookie:cookie},signal:AbortSignal.timeout(5000)});
 console.log(route,response.status);if(response.ok){const body=await response.json();if(body.providers)console.log(JSON.stringify(body.providers));else console.log(JSON.stringify({status:body.status,configured:body.configured,available:body.available}));}
}
const page=await fetch(origin+'/api/whale/chatgpt',{headers:{Cookie:cookie}});
if(page.status!==200||!(await page.text()).includes('登录 ChatGPT'))throw Error('Login page unavailable');
console.log('Authenticated login page OK; unauthenticated API requests rejected.');
