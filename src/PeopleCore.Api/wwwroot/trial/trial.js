const $=id=>document.getElementById(id), out=$('out'), persona=$('persona'), key=$('key');
const ids={EMP:'22222222-2222-4222-8222-222222222222',MGR:'11111111-1111-4111-8111-111111111111',HR:'33333333-3333-4333-8333-333333333333',PAY:'44444444-4444-4444-8444-444444444444',ADMIN:'55555555-5555-4555-8555-555555555555'};
key.value=sessionStorage.getItem('pcTrialKey')||''; persona.value=sessionStorage.getItem('pcTrialPersona')||'TRIAL-EMP';
$('save').onclick=()=>{sessionStorage.setItem('pcTrialKey',key.value);sessionStorage.setItem('pcTrialPersona',persona.value);show('Trial key kept in sessionStorage for this tab only. Persona: '+persona.value)};
$('clear').onclick=()=>out.textContent='';
function show(v){out.textContent=typeof v==='string'?v:JSON.stringify(v,null,2)}
async function api(method,path,body){
 const headers={'X-Trial-Staff-Code':persona.value,'X-Trial-Key':key.value}; if(body!==undefined)headers['Content-Type']='application/json';
 const started=performance.now(); let r;
 try{r=await fetch(path,{method,headers,body:body===undefined?undefined:JSON.stringify(body)}); const txt=await r.text(); let data; try{data=txt?JSON.parse(txt):null}catch{data=txt}
 show({request:{method,path,persona:persona.value},response:{status:r.status,ok:r.ok,durationMs:Math.round(performance.now()-started),correlationId:r.headers.get('X-Correlation-ID'),peopleCoreVersion:r.headers.get('X-PeopleCore-Version'),trial:r.headers.get('X-PeopleCore-Trial'),body:data}});return {r,data};
 }catch(e){show({networkError:String(e)});throw e}
}
$('health').onclick=async()=>{const r=await fetch('/health/startup');show({status:r.status,body:await r.json()})};
$('me').onclick=async()=>{const x=await api('GET','/api/v1/identity/me');if(x.r.ok)$('identity').textContent=`${x.data.staffCode} | roles ${(x.data.roles||[]).join(', ')} | ${x.data.identitySource}`};
$('send').onclick=()=>{let b;const raw=$('body').value.trim();if(raw){try{b=JSON.parse(raw)}catch(e){return show('Invalid JSON: '+e)}}return api($('method').value,$('path').value,b)};
document.querySelectorAll('[data-call]').forEach(btn=>btn.onclick=()=>{const [m,p]=btn.dataset.call.split('|');let b; if(btn.dataset.json)b=JSON.parse(btn.dataset.json); $('method').value=m;$('path').value=p;$('body').value=b?JSON.stringify(b,null,2):'';return api(m,p,b)});
