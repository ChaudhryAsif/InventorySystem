document.addEventListener('DOMContentLoaded', () => {
    const app=document.getElementById('gatePassApp'); if(!app)return;
    const direction=app.dataset.direction, id=Number(app.dataset.id||0);
    const byId=id=>document.getElementById(id), esc=window.escapeHtml;
    let page=1, revision=1, branchId=1, busy=false, request=0, dirty=false, pickerId=0, activePicker=null;
    let selectedGroupId=null,selectedParty=null;const groupOptions=new Map(),itemPickers=new Map();
    document.addEventListener('pointerdown',event=>{if(activePicker&&!activePicker.wrapper.contains(event.target))activePicker.close();});
    const number=n=>(Number(n)||0).toLocaleString(undefined,{maximumFractionDigits:3});
    const passNo=g=>(g.direction==='Inward'?'IGP-':'OGP-')+String(g.id).padStart(6,'0');
    const strings=['PartyName','PartyAddress','PartyPhone','VehicleName','VehicleNumber','TransportCompany','BiltyNumber','DriverName','DriverPhone','IssuedBy','DriverSignature','GateKeeper','ReceivedBy','ReceiverReference','Remarks'];
    const dates=['ArrivalAt','DepartureAt'], weights=['GrossWeightKg','TareWeightKg'],originalDates={};
    const camel=s=>s[0].toLowerCase()+s.slice(1);
    const partyTypes=[['all','All parties & groups'],['supplier','Suppliers'],['customer','Customers'],['RawMaterial','Raw material groups'],['FinishedGoods','Finished goods groups']];
    const itemTypes=[['all','All item types'],['RawMaterial','Raw Material'],['FinishedGoods','Finished Goods']];
    async function json(url,options){
        const response=await fetch(url,{...options,headers:{Accept:'application/json','X-Requested-With':'XMLHttpRequest',...options?.headers}});
        const body=await response.text();let value;
        try{value=body?JSON.parse(body):null;}catch{}
        const message=value?.message||(response.status===401||response.redirected?'Your session expired. Please sign in again.':response.status===403?'You do not have permission for this Gate Pass action.':'The request could not be completed. Please refresh and try again.');
        if(!response.ok||!value||value.success===false)throw new Error(message);
        return value;
    }
    async function load(){
        if(byId('gpFrom').value&&byId('gpTo').value&&byId('gpFrom').value>byId('gpTo').value){
            byId('gpTo').setCustomValidity('Choose an end date on or after the start date.');byId('gpTo').reportValidity();return;
        }
        const current=++request;
        byId('gpTo').setCustomValidity('');
        const qs=new URLSearchParams({direction,page:String(page),search:byId('gpSearch').value.trim(),party:byId('gpParty').value.trim(),status:byId('gpStatus').value});
        if(byId('gpFrom').value)qs.set('from',byId('gpFrom').value);if(byId('gpTo').value)qs.set('to',byId('gpTo').value);
        byId('gpPrevious').disabled=byId('gpNext').disabled=true;byId('gpPage').textContent='Loading…';
        byId('gpList').innerHTML='<tr><td colspan="7">Loading gate passes…</td></tr>';
        try{
            const data=await json('/GatePass/GetList?'+qs);if(current!==request)return;
            const pages=Math.max(1,Math.ceil(data.totalCount/data.pageSize));
            if(page>pages){page=pages;return load();}
            byId('gpList').innerHTML=data.data.length?data.data.map(g=>`<tr><td>${passNo(g)}</td><td>${g.passDate.split('T')[0]}</td><td>${esc(g.partyName)}</td><td>${esc(g.vehicleNumber||'—')}</td><td>${number(g.itemCount)}</td><td><span class="gp-status ${g.isCancelled?'cancelled':''}">${g.isCancelled?'Cancelled':'Active'}</span></td><td><div class="gp-actions"><a class="gp-button gp-secondary" target="_blank" rel="noopener" href="/GatePass/Print/${g.id}">View / Print</a>${g.isCancelled?'':`${byId('gatePassApp').dataset.canEdit==='true'?`<a class="gp-button gp-secondary" href="/GatePass/Create?direction=${direction}&id=${g.id}">Edit</a>`:''}<button class="gp-button gp-danger" data-cancel="${g.id}" data-revision="${g.revision}" title="Delete by cancelling this pass; its history is retained">Delete</button>`}</div></td></tr>`).join(''):'<tr><td colspan="7">No matching gate passes. Change the filters or create a new pass.</td></tr>';
            byId('gpPage').textContent=`Page ${page} of ${pages} · ${data.totalCount} records`;
            byId('gpPrevious').disabled=page<=1;byId('gpNext').disabled=page>=pages;
        }catch(e){if(current===request){byId('gpPage').textContent='Unable to load gate passes';byId('gpList').innerHTML=`<tr><td colspan="7"><div class="gp-notice gp-error" role="alert">${esc(e.message)} <button type="button" class="gp-button gp-secondary" id="gpRetry">Retry</button></div></td></tr>`;byId('gpRetry').onclick=load;}}
    }
    function picker(input,endpoint,types,onSelect){
        const wrapper=document.createElement('div');wrapper.className='gp-picker';input.replaceWith(wrapper);wrapper.append(input);
        const toggle=document.createElement('button');toggle.type='button';toggle.className='gp-picker-toggle';toggle.textContent='▾';toggle.setAttribute('aria-label',endpoint.includes('Party')?'Choose party':'Choose inventory item');
        const panel=document.createElement('div');panel.className='gp-options';panel.hidden=true;
        panel.innerHTML=`<select aria-label="${endpoint.includes('Party')?'Party':'Item'} type">${types.map(([v,t])=>`<option value="${v}">${t}</option>`).join('')}</select><div class="gp-option-list" role="listbox" aria-label="Matches"></div><div class="gp-options-footer"><span class="gp-help">Choose a match, or keep your typed value.</span><button type="button" class="gp-more" hidden>More matches</button></div>`;
        if(endpoint.includes('/GetItemOptions'))panel.querySelector('.gp-help').textContent='Choose an item from the selected group.';
        const listId='gpOptions'+(++pickerId);panel.querySelector('.gp-option-list').id=listId;
        wrapper.append(toggle,panel);input.setAttribute('role','combobox');input.setAttribute('aria-autocomplete','list');input.setAttribute('aria-haspopup','listbox');input.setAttribute('aria-expanded','false');input.setAttribute('aria-controls',listId);input.autocomplete='off';
        const type=panel.querySelector('select'),list=panel.querySelector('.gp-option-list'),more=panel.querySelector('.gp-more');
        let timer, version=0, lookupPage=1, lookupBusy=false;
        const handle={wrapper,close};
        const isItem=endpoint.includes("/GetItemOptions");
        function close(){panel.hidden=true;input.setAttribute('aria-expanded','false');version++;clearTimeout(timer);lookupBusy=false;if(activePicker===handle)activePicker=null;}
        async function search(append=false){
            if(activePicker&&activePicker!==handle)activePicker.close();activePicker=handle;
            clearTimeout(timer);const current=++version,lookupGroup=selectedGroupId;lookupBusy=true;if(!append){lookupPage=1;list.textContent='Searching…';}
            panel.hidden=false;input.setAttribute('aria-expanded','true');
            if(append&&document.activeElement===more)input.focus();
            if(!append)more.hidden=true;more.disabled=true;
            if(isItem&&!lookupGroup){list.textContent='Select an item group first.';more.hidden=true;lookupBusy=false;return;}
            try{
                const url=new URL(endpoint,location.href);url.searchParams.set('search',input.value.trim());url.searchParams.set('type',type.value);url.searchParams.set('page',lookupPage);if(isItem)url.searchParams.set('groupId',lookupGroup);
                const data=await json(url.pathname+url.search);
                if(current!==version||(isItem&&lookupGroup!==selectedGroupId))return;if(!append)list.replaceChildren();
                for(const option of data.data){
                    const button=document.createElement('button');button.type='button';button.className='gp-option';button.setAttribute('role','option');button.setAttribute('aria-selected','false');
                    const label=option.type==='RawMaterial'?'Raw Material':option.type==='FinishedGoods'?'Finished Goods':option.type;
                    button.innerHTML=`<strong>${esc(option.name)}</strong><small>${esc([option.detail,label,option.unit?.toUpperCase()].filter(Boolean).join(' · '))}</small>`;
                    button.onclick=()=>{if(isItem&&String(option.groupId)!==selectedGroupId){close();return;}input.value=option.name;input.setCustomValidity('');input.removeAttribute('aria-invalid');onSelect?.(option);input.dispatchEvent(new Event('change',{bubbles:true}));close();input.focus();};list.append(button);
                }
                if(!list.children.length)list.textContent=isItem?'No matching items in the selected group.':'No matches. You can enter a value manually.';
                if(!data.hasMore&&document.activeElement===more)input.focus();
                more.hidden=!data.hasMore;more.disabled=false;lookupBusy=false;
            }catch(e){if(current!==version)return;lookupBusy=false;more.disabled=false;if(append)lookupPage--;else list.textContent='';const message=document.createElement('p');message.className='gp-help';message.textContent=e.message+(isItem?' Reopen to retry.':' Reopen to retry, or enter a value manually.');list.append(message);}
        }
        toggle.onclick=()=>{if(panel.hidden){search();input.focus();}else close();};
        input.addEventListener('input',()=>{version++;clearTimeout(timer);more.hidden=true;list.textContent='Searching…';timer=setTimeout(()=>search(),220);});
        input.addEventListener('keydown',event=>{if(event.key==='ArrowDown'){event.preventDefault();if(panel.hidden)search();else list.querySelector('button')?.focus();}if(event.key==='Escape'){event.preventDefault();close();}});
        panel.addEventListener('keydown',event=>{
            if(event.key==='Escape'){event.preventDefault();close();input.focus();}
            if(event.key==='ArrowDown'||event.key==='ArrowUp'){const buttons=[...list.querySelectorAll('button')],index=buttons.indexOf(document.activeElement);if(index>=0){event.preventDefault();buttons[Math.max(0,Math.min(buttons.length-1,index+(event.key==='ArrowDown'?1:-1)))].focus();}}
        });
        type.onchange=()=>search();more.onclick=()=>{if(!lookupBusy){lookupPage++;search(true);}};
        wrapper.addEventListener('focusout',()=>setTimeout(()=>{if(!wrapper.contains(document.activeElement))close();},0));
        return handle;
    }
    async function loadGroups(){
        const select=byId('gpGroupId');
        const data=await json('/GatePass/GetGroupOptions'+(id?'?passId='+id:''));
        select.replaceChildren(new Option('Select an item group...', ''));
        for(const group of data.data){groupOptions.set(String(group.groupId),group);select.add(new Option(group.name+' — '+(group.type==='RawMaterial'?'Raw Material':'Finished Goods')+(group.isActive?'':' (saved, inactive)'),group.groupId));}
        select.disabled=false;
    }
    function filterGroupOptions(){
        const type=byId('gpGroupType').value,select=byId('gpGroupId'),saved=selectedGroupId;
        select.replaceChildren(new Option('Select an item group...', ''));
        for(const group of groupOptions.values())if(!type||group.type===type)select.add(new Option(group.name+' — '+(group.type==='RawMaterial'?'Raw Material':'Finished Goods')+(group.isActive?'':' (saved, inactive)'),group.groupId));
        select.value=saved||'';
    }
    function chooseGroup(value,fillParty=false){
        const next=value?String(value):null;
        if(next===selectedGroupId)return;
        for(const picker of itemPickers.values())picker.close();
        selectedGroupId=next;
        const selected=groupOptions.get(next);if(selected&&byId('gpGroupType').value&&byId('gpGroupType').value!==selected.type){byId('gpGroupType').value=selected.type;filterGroupOptions();}
        byId('gpGroupId').value=next||'';
        let removed=0;
        for(const row of [...byId('gpLines').children]){
            if(!next||row.dataset.groupId!==next){if(row.dataset.itemId||row.querySelector('.gp-description').value.trim())removed++;itemPickers.delete(row);row.remove();}
        }
        if(!byId('gpLines').children.length)addLine();else renumber();
        const group=groupOptions.get(next);
        byId('gpGroupHelp').textContent=group?(removed?'Items from the previous group were cleared. ':'')+'Only '+group.name+' items are available.': 'Select a group before choosing items.';
        if(fillParty&&group){byId('PartyName').value=group.name;byId('PartyPhone').value=group.phone||'';byId('PartyAddress').value=group.address||'';selectedParty={name:group.name,phone:group.phone||'',address:group.address||''};}
        dirty=true;
    }
    function inwardAmount(qty,price){
        // Multiply at the saved quantity/price precision, then round cents exactly.
        const q=Number(qty),p=Number(price);if(!Number.isFinite(q)||!Number.isFinite(p)||Math.abs(q)>100000000||Math.abs(p)>100000000)return '';
        const product=BigInt(q.toFixed(3).replace('.',''))*BigInt(p.toFixed(4).replace('.',''));
        const cents=((product<0n?-product:product)+50000n)/100000n;
        return (product<0n?'-':'')+(cents/100n)+'.'+String(cents%100n).padStart(2,'0');
    }
    function addLine(line={}){
        if(byId('gpLines').children.length>=100)return;
        const row=document.createElement('tr');row.dataset.lineId=String(line.id||0);row.dataset.itemId=line.itemId==null?'':String(line.itemId);row.dataset.groupId=String(line.itemGroupId??selectedGroupId??'');row.innerHTML=`<td class="gp-serial" data-label="Item"></td><td data-label="Description"><input class="gp-description" maxlength="300" aria-label="Item description" placeholder="Search selected group items" required value="${esc(line.description||'')}" /></td><td data-label="Packing Type"><select class="gp-packing" aria-label="Packing type"><option value="">None</option><option>3-Ply</option><option>5-Ply</option></select></td><td data-label="Packing Qty"><select class="gp-packing-qty" aria-label="Packing quantity"><option value="0">Select qty</option>${[100,50,30,20,10].map(q=>`<option value="${q}">${q}</option>`).join('')}</select></td><td data-label="Bundle Qty"><input class="gp-bundles" aria-label="Bundle quantity" type="number" min="0" max="100000000" step="1" value="${line.bundleQuantity??0}" /></td><td data-label="Open Ctn"><input class="gp-open" aria-label="Open cartons" type="number" min="0" max="100000000" step="1" value="${line.openCartonQuantity??0}" /></td><td data-label="Total Qty"><input class="gp-total" aria-label="Total quantity" type="number" min="0.001" max="100000000" step="0.001" value="${line.totalQuantity??0}" required /></td><td data-label="Unit"><select class="gp-unit" aria-label="Unit"><option value="pcs">PCS</option><option value="kg">KG</option><option value="in">Inches</option></select></td><td data-label="Unit Price"><input class="gp-price" aria-label="Unit price" type="number" min="0" max="100000000" step="0.0001" value="${line.unitPrice??''}" placeholder="Not recorded" /><small class="gp-price-unit"></small></td><td data-label="Remove"><button type="button" class="gp-button gp-danger gp-remove" aria-label="Remove item">×</button></td>`;
        const unitCell=row.querySelector('.gp-unit').closest('td');row.children[1].after(unitCell);
        if(direction==='Outward')row.querySelector('.gp-price').closest('td').hidden=true;
        if(direction==='Inward'){
            for(const cls of ['.gp-packing','.gp-packing-qty','.gp-bundles','.gp-open'])row.querySelector(cls).closest('td').hidden=true;
            row.querySelector('.gp-total').closest('td').dataset.label='Net Weight / Quantity';
            row.querySelector('.gp-price').closest('td').dataset.label='Purchase Price / Unit';
            const amount=document.createElement('td');amount.dataset.label='Total';amount.innerHTML='<input class="gp-amount" aria-label="Line amount" readonly />';row.querySelector('.gp-remove').closest('td').before(amount);
        }
        row.querySelector('.gp-packing').value=line.packingType||'';row.querySelector('.gp-unit').value=line.unit||'pcs';
        const packing=row.querySelector('.gp-packing-qty'),bundles=row.querySelector('.gp-bundles'),open=row.querySelector('.gp-open'),total=row.querySelector('.gp-total'),unit=row.querySelector('.gp-unit');
        if(line.packingQuantity&&!['0','10','20','30','50','100'].includes(String(line.packingQuantity)))packing.add(new Option(line.packingQuantity+' (saved)',line.packingQuantity));packing.value=String(line.packingQuantity??0);
        row.dataset.calculate=String(line.calculateFromBundles??(line.totalQuantity===undefined));
        function calculate(changed=false){
            const cartons=direction==='Outward'&&unit.value==='pcs';row.querySelector('.gp-price-unit').textContent='Price / '+unit.value.toUpperCase();total.setAttribute('aria-label','Quantity ('+unit.value.toUpperCase()+')');if(changed&&cartons)row.dataset.calculate='true';
            open.disabled=!cartons;bundles.step=cartons&&row.dataset.calculate==='true'?'1':'0.001';
            total.readOnly=cartons&&row.dataset.calculate==='true';
            total.title=total.readOnly?'Packing quantity × bundles + open cartons':'Enter the actual quantity. Changing carton packing quantities enables automatic totals.';
            if(total.readOnly)total.value=Number(packing.value)*Number(bundles.value)+Number(open.value);
            const amount=row.querySelector('.gp-amount'),price=row.querySelector('.gp-price');if(amount)amount.value=price.value===''?'':inwardAmount(total.value,price.value);
            packing.setCustomValidity(total.readOnly&&Number(bundles.value)>0&&Number(packing.value)<=0?'Select the number of cartons per bundle.':'');
            open.setCustomValidity(total.readOnly&&(Number(total.value)<=0||Number(total.value)>100000000)?'Enter bundles or open cartons to produce a total between 1 and 100,000,000.':'');
        }
        total.addEventListener('input',()=>calculate());row.querySelector('.gp-price').addEventListener('input',()=>calculate());
        [packing,bundles,open].forEach(input=>input.addEventListener(input.tagName==='SELECT'?'change':'input',()=>calculate(true)));
        unit.addEventListener('change',()=>{row.dataset.calculate=String(unit.value==='pcs');if(unit.value!=='pcs'){open.value='0';if(!Number(total.value))total.value='1';}calculate();});
        calculate();
        row.querySelector('.gp-remove').onclick=()=>{if(byId('gpLines').children.length>1){lookup.close();itemPickers.delete(row);row.remove();renumber();dirty=true;}};
        byId('gpLines').appendChild(row);
        unit.disabled=Boolean(line.itemId);
        let selectedName=line.itemId?line.description:null;
        row.querySelector('.gp-description').addEventListener('input',()=>{if(selectedName!==null){row.dataset.itemId='';row.dataset.groupId='';unit.disabled=false;row.querySelector('.gp-price').value='';selectedName=null;}});
        const lookup=picker(row.querySelector('.gp-description'),'/GatePass/GetItemOptions',itemTypes,option=>{
            const sameItem=row.dataset.itemId===String(option.itemId);
            row.dataset.itemId=String(option.itemId);row.dataset.groupId=String(option.groupId);selectedName=option.name;unit.value=option.unit;unit.disabled=true;
            if(!sameItem){packing.value='0';bundles.value='0';open.value='0';total.value='0';row.dataset.calculate=String(option.unit==='pcs'&&(option.packingQuantity!=null||Boolean(option.packingType)));}
            row.querySelector('.gp-price').value=(direction==='Inward'?option.purchasePrice:option.salePrice)??'';
            row.querySelector('.gp-packing').value=option.packingType||'';
            if(option.packingQuantity!=null)packing.value=String(option.packingQuantity);
            calculate();
        });itemPickers.set(row,lookup);renumber();
    }
    function renumber(){const rows=[...byId('gpLines').children];rows.forEach((row,i)=>{row.querySelector('.gp-serial').textContent=i+1;row.querySelector('.gp-remove').disabled=rows.length===1;});byId('gpAddLine').disabled=!selectedGroupId||rows.length>=100;}
    function netWeight(){const gross=byId('GrossWeightKg').value,tare=byId('TareWeightKg').value;byId('NetWeightKg').value=gross!==''&&tare!==''?number(Number(gross)-Number(tare)):'';byId('TareWeightKg').setCustomValidity(gross!==''&&tare!==''&&Number(tare)>Number(gross)?'Tare weight cannot exceed gross weight.':'');}
    async function suggestions(){
        try{const data=await json('/GatePass/GetSuggestions');for(const key of ['VehicleName','VehicleNumber','TransportCompany','DriverName','DriverPhone','GateKeeper','ReceivedBy']){const list=document.createElement('datalist');list.id='gpHistory'+key;for(const value of data[camel(key)]){const option=document.createElement('option');option.value=value;list.append(option);}byId(key).setAttribute('list',list.id);app.append(list);}}
        catch{const note=document.createElement('p');note.className='gp-help';note.textContent='Recent transport suggestions are unavailable. You can enter details manually.';byId('VehicleName').closest('.gp-card').append(note);}
    }
    async function initForm(){
        const today=new Date();byId('PassDate').value=`${today.getFullYear()}-${String(today.getMonth()+1).padStart(2,'0')}-${String(today.getDate()).padStart(2,'0')}`;

        picker(byId('PartyName'),'/GatePass/GetPartyOptions',partyTypes,option=>{if(option.groupId)chooseGroup(option.groupId);byId('PartyPhone').value=option.phone||'';byId('PartyAddress').value=option.address||'';selectedParty={name:option.name,phone:option.phone||'',address:option.address||''};});
        byId('PartyName').addEventListener('input',()=>{if(selectedParty&&byId('PartyName').value!==selectedParty.name){if(byId('PartyPhone').value===selectedParty.phone)byId('PartyPhone').value='';if(byId('PartyAddress').value===selectedParty.address)byId('PartyAddress').value='';selectedParty=null;}});
        byId('gpAddLine').onclick=()=>{addLine();dirty=true;byId('gpLines').lastElementChild.querySelector('input').focus();};
        weights.forEach(k=>byId(k).addEventListener('input',netWeight));
        const arrival=byId('ArrivalAt'),departure=byId('DepartureAt');for(const input of [arrival,departure])input.addEventListener('input',()=>departure.setCustomValidity(arrival.value&&departure.value&&departure.value<arrival.value?'Departure must be on or after arrival.':''));
        let previousDriver='';byId('DriverName').addEventListener('input',()=>{const signature=byId('DriverSignature');if(!signature.value||signature.value===previousDriver)signature.value=byId('DriverName').value;previousDriver=byId('DriverName').value;});
        byId('gpForm').addEventListener('submit',save);
        const markDirty=event=>{if(!event.target.closest('.gp-options'))dirty=true;};
        byId('gpForm').addEventListener('input',markDirty);byId('gpForm').addEventListener('change',markDirty);
        byId('gpForm').addEventListener('invalid',event=>event.target.setAttribute('aria-invalid','true'),true);
        byId('gpForm').addEventListener('input',event=>{if(event.target.validity?.valid)event.target.removeAttribute('aria-invalid');});
        window.addEventListener('beforeunload',event=>{if(dirty&&!busy){event.preventDefault();event.returnValue='';}});
        byId('gpSave').disabled=byId('gpSavePrint').disabled=true;
        byId('gpGroupType').addEventListener('change',()=>{if(selectedGroupId&&groupOptions.get(selectedGroupId)?.type!==byId('gpGroupType').value&&byId('gpGroupType').value)chooseGroup(null);filterGroupOptions();});
        byId('gpGroupId').addEventListener('change',()=>chooseGroup(byId('gpGroupId').value,true));
        try{await loadGroups();}catch(e){byId('gpFormError').textContent=e.message;byId('gpFormError').hidden=false;return;}
        if(!id){addLine();suggestions();byId('gpSave').disabled=byId('gpSavePrint').disabled=false;return;}
        byId('gpSave').disabled=byId('gpSavePrint').disabled=true;
        try{
            const g=await json('/GatePass/GetById?id='+id);if(g.direction!==direction||g.isCancelled)throw new Error('This gate pass cannot be edited. Return to the list.');
            revision=g.revision;branchId=g.branchId;byId('gpNumber').value=g.passNumber;byId('PassDate').value=g.passDate.split('T')[0];
            if(g.issuedBy&&![...byId('IssuedBy').options].some(o=>o.value===g.issuedBy))byId('IssuedBy').add(new Option(g.issuedBy+' (saved)',g.issuedBy));
            strings.forEach(k=>byId(k).value=g[camel(k)]||'');dates.forEach(k=>{byId(k).value=(g[camel(k)]||'').slice(0,19);originalDates[k]={display:byId(k).value,raw:g[camel(k)]};});weights.forEach(k=>byId(k).value=g[camel(k)]??'');previousDriver=g.driverName||'';
            selectedParty={name:g.partyName,phone:g.partyPhone||'',address:g.partyAddress||''};
            selectedGroupId=g.groupId?String(g.groupId):null;
            byId('gpGroupId').required=Boolean(g.groupId);byId('gpGroupType').value=groupOptions.get(selectedGroupId)?.type||'';filterGroupOptions();byId('gpGroupId').value=selectedGroupId||'';
            byId('gpGroupHelp').textContent=g.groupId?'Only items from this saved group are available.':'This older pass has no saved group. Select a group before choosing additional items.';
            g.lines.forEach(addLine);netWeight();byId('gpSave').disabled=byId('gpSavePrint').disabled=false;dirty=false;suggestions();
        }catch(e){byId('gpFormError').textContent=e.message;byId('gpFormError').hidden=false;byId('gpForm').querySelectorAll('input,select,textarea,button').forEach(el=>el.disabled=true);}
    }
    async function save(event){
        event.preventDefault();if(busy)return;
        const model={id,revision,direction,groupId:selectedGroupId?Number(selectedGroupId):null,branchId:id?branchId:Number(document.getElementById('posBranch')?.value||1),passDate:byId('PassDate').value};
        strings.forEach(k=>model[camel(k)]=byId(k).value.trim()||null);dates.forEach(k=>model[camel(k)]=originalDates[k]?.display===byId(k).value?originalDates[k].raw:byId(k).value||null);weights.forEach(k=>model[camel(k)]=byId(k).value===''?null:Number(byId(k).value));
        model.lines=[...byId('gpLines').children].map(row=>({id:Number(row.dataset.lineId||0),itemId:row.dataset.itemId?Number(row.dataset.itemId):null,unitPrice:row.querySelector('.gp-price').value===''?null:Number(row.querySelector('.gp-price').value),description:row.querySelector('.gp-description').value.trim(),packingType:row.querySelector('.gp-packing').value||null,packingQuantity:Number(row.querySelector('.gp-packing-qty').value),bundleQuantity:Number(row.querySelector('.gp-bundles').value),openCartonQuantity:Number(row.querySelector('.gp-open').value),calculateFromBundles:direction==='Outward'&&row.querySelector('.gp-unit').value==='pcs'&&row.dataset.calculate==='true',totalQuantity:Number(row.querySelector('.gp-total').value),unit:row.querySelector('.gp-unit').value}));
        if(!model.partyName||model.lines.some(line=>!line.description)){const input=!model.partyName?byId('PartyName'):[...byId('gpLines').querySelectorAll('.gp-description')].find(el=>!el.value.trim());input.setCustomValidity('Enter a name or select a match.');input.reportValidity();input.addEventListener('input',()=>input.setCustomValidity(''),{once:true});return;}
        if(model.groupId&&model.lines.some(line=>!line.itemId)){const notice=byId('gpFormError');notice.textContent='Choose every item from the selected group using its item dropdown.';notice.hidden=false;return;}
        busy=true;byId('gpSave').disabled=byId('gpSavePrint').disabled=true;byId('gpFormError').hidden=true;
        // The print tab is opened now, while the click still counts, then pointed at the pass after the save.
        const print=event.submitter?.id==='gpSavePrint',printWin=print?window.open('','_blank'):null,button=print?byId('gpSavePrint'):byId('gpSave'),label=button.textContent;button.textContent='Saving…';
        let saved=false;
        try{
            const result=await json('/GatePass/Save',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(model)});
            dirty=false;saved=true;
            if(printWin){printWin.location.href='/GatePass/Print/'+result.id;location.href='/GatePass/'+direction;}
            else location.href=print?'/GatePass/Print/'+result.id:'/GatePass/'+direction;
        }catch(e){if(printWin)printWin.close();const notice=byId('gpFormError');notice.textContent=e.message;notice.hidden=false;notice.scrollIntoView({behavior:'smooth',block:'center'});}
        finally{if(!saved){busy=false;byId('gpSave').disabled=byId('gpSavePrint').disabled=false;button.textContent=label;}}
    }
    if(app.dataset.mode==='list'){
        picker(byId('gpParty'),'/GatePass/GetPassPartyOptions?direction='+direction,[['all','Parties on saved passes']],()=>{page=1;load();});
        byId('gpFilter').onclick=()=>{page=1;load();};for(const key of ['gpSearch','gpParty'])byId(key).addEventListener('keydown',e=>{if(e.key==='Enter'){page=1;load();}});
        byId('gpTo').oninput=()=>byId('gpTo').setCustomValidity('');
        byId('gpStatus').onchange=()=>{page=1;load();};byId('gpClear').onclick=()=>{['gpSearch','gpParty','gpFrom','gpTo'].forEach(k=>byId(k).value='');byId('gpStatus').value='active';page=1;load();};
        byId('gpPrevious').onclick=()=>{if(!byId('gpPrevious').disabled){page--;load();}};byId('gpNext').onclick=()=>{if(!byId('gpNext').disabled){page++;load();}};
        byId('gpList').addEventListener('click',async event=>{const button=event.target.closest('[data-cancel]');if(!button||button.disabled)return;
            const reason=await window.requestOperationReason('Delete gate pass — cancellation reason','Delete gate pass');if(!reason)return;button.disabled=true;
            try{await json('/GatePass/Delete',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id:Number(button.dataset.cancel),revision:Number(button.dataset.revision),reason})});await load();}
            catch(e){await alertDialog(e.message,{type:'error'});button.disabled=false;}
        });load();
    }else{initForm();}
});

