export const name='whale-model-health';
export const inject=['llm','credentials','connection'];
export function apply(ctx){
 ctx.effect(()=>ctx.connection.fetch.register({path:'/api/whale/models',methods:['GET'],requestBody:'buffered',fetch:async()=>{
   const providers=ctx.llm.listProviders().map(p=>({id:p.id,name:p.name}));
   let models=[],error;try{models=(await ctx.llm.listModels('openai-codex')).map(m=>({id:m.id,name:m.name}));}catch(e){error=e.code||e.name;}
   const grant=await ctx.credentials.readRecord('llm-pi-ai/openai-codex');
   return Response.json({providers,models,error,authorized:grant?.kind==='grant'&&grant?.payload?.type==='oauth'},{headers:{'Cache-Control':'no-store'}});
 }}),'whale model diagnostics');
}
