const $=s=>document.querySelector(s);let cat=null;
const esc=t=>String(t??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const NA='<span class="na">Information unavailable</span>';
function toast(m){const t=$('#toast');t.textContent=m;t.classList.add('on');setTimeout(()=>t.classList.remove('on'),2500)}
async function api(url,opt){try{const r=await fetch(url,opt);const j=await r.json();return{ok:r.ok,data:j}}
  catch{return{ok:false,data:{message:'Cannot reach the server. Check your connection and try again.'}}}}
async function loadCats(){const{ok,data}=await api('/api/categories');if(!ok)return;
  $('#cats').innerHTML=[`<button data-id="" aria-pressed="true">All</button>`,...data.map(c=>`<button data-id="${c.CategoryId}" data-n="${esc(c.Name)}" aria-pressed="false">${esc(c.Name)}</button>`)].join('')}
$('#cats').onclick=e=>{const b=e.target.closest('button');if(!b)return;cat=b.dataset.id||null;
  document.querySelectorAll('#cats button').forEach(x=>x.setAttribute('aria-pressed',x===b));load()};
$('#sf').onsubmit=e=>{e.preventDefault();const q=$('#q').value.trim();
  if(!q){$('#msg').textContent='Please enter a place name or keyword to search.';return}load(q)};
$('#q').oninput=e=>{if(!e.target.value){$('#msg').textContent='';load()}};
async function load(q){const p=new URLSearchParams();if(q)p.set('search',q);if(cat)p.set('categoryId',cat);const so=$('#sort').value;if(so)p.set('sort',so);
  const{ok,data}=await api('/api/attractions?'+p);const g=$('#grid');
  if(!ok){g.innerHTML='';$('#msg').textContent=data.message;return}
  $('#msg').textContent=data.length?`${data.length} place${data.length>1?'s':''} found`:'No results found. Try another search or choose a different category.';
  g.innerHTML=data.map(a=>`<article class="card" data-c="${esc(a.Category)}">${a.Image?`<img src="${esc(a.Image)}" alt="${esc(a.Name)}">`:`<div class="ph"><span>${esc(a.Category)}</span></div>`}
   <div class="cb"><span class="meta">${esc(a.Category)}</span>${a.DistanceKm!=null?`<span class="dist">${(+a.DistanceKm).toFixed(1)} km from Hikkaduwa</span>`:''}
   <h3>${esc(a.Name)}</h3><p>${esc(a.Description)}</p>
   <div class="acts"><button data-d="${a.AttractionId}">View details</button><button class="ghost" data-a="${a.AttractionId}">Add to plan</button></div></div></article>`).join('')}
$('#grid').onclick=e=>{const b=e.target.closest('button');if(!b)return;if(b.dataset.d)details(b.dataset.d);if(b.dataset.a)add(b.dataset.a)};
async function details(id){const{ok,data:a}=await api('/api/attractions/'+id);if(!ok)return toast(a.message);
  const v=x=>x?esc(x):NA,map=a.Latitude!=null?`<iframe title="Map of ${esc(a.Name)}" width="100%" height="220" style="border:0;border-radius:8px" loading="lazy" src="https://maps.google.com/maps?q=${a.Latitude},${a.Longitude}&z=14&output=embed"></iframe><br><a href="https://www.google.com/maps/search/?api=1&query=${a.Latitude},${a.Longitude}" target="_blank" rel="noopener">Open in Google Maps</a>`:NA;
  $('#dlg').innerHTML=`<h2>${esc(a.Name)}</h2><p class="meta" data-c="${esc(a.Category)}">${esc(a.Category)}</p>
   ${a.Images.map(i=>`<img src="${esc(i.ImagePath)}" alt="${esc(i.Caption||a.Name)}" style="max-width:100%;border-radius:8px">`).join('')}
   <p>${esc(a.Description)}</p><dl><dt>Location</dt><dd>${map}</dd>
   <dt>Distance</dt><dd>${a.DistanceKm!=null?(+a.DistanceKm).toFixed(1)+' km from Hikkaduwa':NA}</dd>
   <dt>Opening times</dt><dd>${v(a.OpeningTimes)}</dd><dt>Entrance fee</dt><dd>${v(a.EntranceFee)}</dd>
   <dt>Time needed</dt><dd>${v(a.VisitDuration)}</dd><dt>Facilities</dt><dd>${v(a.Facilities)}</dd><dt>Travel tips</dt><dd>${v(a.TravelTips)}</dd><dt>Visitor info</dt><dd>${v(a.VisitorInfo)}</dd></dl>
   <div class="acts"><button data-a="${a.AttractionId}">Add to plan</button><button class="ghost" data-x>Close</button></div>`;
  $('#dlg').showModal()}
$('#dlg').onclick=e=>{const b=e.target.closest('button');if(!b)return;if(b.dataset.a)add(b.dataset.a);if(b.dataset.x||b.dataset.a)$('#dlg').close()};
async function add(id){const{data}=await api('/api/itinerary/'+id,{method:'POST'});toast(data.message);loadTrip()}
async function loadTrip(){const{ok,data}=await api('/api/itinerary');if(!ok)return;$('#count').textContent=data.length;
  $('#tripMsg').textContent=data.length?'':'Your plan is empty. Select attractions first, then come back here.';
  $('#tripList').innerHTML=data.map(a=>`<li><b>${esc(a.Name)}</b><br><small>${a.DistanceKm!=null?(+a.DistanceKm).toFixed(1)+' km · ':''}${a.OpeningTimes?esc(a.OpeningTimes):'Opening times unavailable'} · ${a.VisitDuration?esc(a.VisitDuration):'time needed unknown'}</small>
   <div class="tb"><button data-m="up" data-i="${a.AttractionId}">Up</button><button data-m="down" data-i="${a.AttractionId}">Down</button><button class="ghost" data-r="${a.AttractionId}">Remove</button></div></li>`).join('')}
$('#tripList').onclick=async e=>{const b=e.target.closest('button');if(!b)return;
  const r=b.dataset.r?await api('/api/itinerary/'+b.dataset.r,{method:'DELETE'}):await api(`/api/itinerary/${b.dataset.i}/move?dir=${b.dataset.m}`,{method:'PUT'});
  if(!r.ok)toast(r.data.message);loadTrip()};
$('#tripBtn').onclick=()=>{$('#trip').hidden=false;loadTrip()};$('#closeTrip').onclick=()=>$('#trip').hidden=true;
$('#sort').onchange=()=>load($('#q').value.trim());
loadCats();load();loadTrip();
