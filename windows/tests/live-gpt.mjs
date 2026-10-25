// Opt-in integration probe; not part of the installed companion.
import {writeFile} from 'node:fs/promises';
export const name='whale-gpt-smoke';
export const inject=['llm'];
export function apply(ctx){
 const controller=new AbortController();
 ctx.effect(()=>{const timer=setTimeout(()=>controller.abort(),45000);void(async()=>{
  const result={provider:'openai-codex',model:'gpt-6-luna',text:'',finish:null};
  try{for await(const chunk of ctx.llm.stream({provider:result.provider,model:result.model,messages:[{role:'user',content:[{type:'text',text:'Reply with exactly OK.'}]}],maxTokens:32,signal:controller.signal})){
    if(chunk.type==='text-delta')result.text+=chunk.text;
    if(chunk.type==='finish')result.finish=chunk.reason;
  }}catch(error){result.errorCode=error.code??error.name;}
  finally{clearTimeout(timer);await writeFile(new URL('../build/live-gpt-result.json',import.meta.url),JSON.stringify(result));}
 })();return()=>{clearTimeout(timer);controller.abort();};},'explicit GPT smoke test');
}
