(() => {
    'use strict';
    const page=document.getElementById('exPage');if(!page)return;
    const $=id=>document.getElementById(id), modal=$('exModal');
    let busy=false, seq=0;
    const iso=d=>d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0');
    const money=n=>Number(n||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
    function message(text,error=false){const m=modal.open?$('exModalMessage'):$('exMessage');m.hidden=!text;m.textContent=text||'';m.classList.toggle('error',error);}
    async function request(url,body){
        const response=await fetch(url,body===undefined?{headers:{Accept:'application/json'}}:{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||'','X-Request-ID':crypto.randomUUID()},body:JSON.stringify(body)});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success)throw Error(result?.message||'Unable to complete the request. Reload to check the saved result.');
        return result;
    }
    function cell(text,cls){const td=document.createElement('td');td.textContent=text;if(cls)td.className=cls;return td;}
    const filters=['exMonth','exFilterCategory','exSearch','exVoid'];
    function controls(){page.querySelectorAll('button,input,select').forEach(el=>{if(!filters.includes(el.id))el.disabled=busy;});}
    async function run(fn){if(busy)return;busy=true;controls();try{await fn();}catch(e){message(e.message,true);}finally{busy=false;controls();}}
    function render(result){
        const body=$('exRows');body.replaceChildren();
        for(const row of result.data){
            const tr=document.createElement('tr');if(row.status==='Void')tr.className='ex-void';
            tr.append(cell(row.expenseDate.slice(0,10)),cell(row.voucherNo),cell(row.category),cell(row.description),cell(money(row.amount),'num'),cell(row.status));
            const td=document.createElement('td');
            if(row.status==='Posted'){const b=document.createElement('button');b.type='button';b.className='pr-secondary';b.textContent='Void';b.addEventListener('click',()=>voidExpense(row));td.appendChild(b);}
            tr.appendChild(td);body.appendChild(tr);
        }
        if(!result.data.length){const tr=document.createElement('tr'),td=cell('No expenses found.');td.colSpan=7;tr.appendChild(td);body.appendChild(tr);}
        const foot=$('exFoot');foot.replaceChildren();
        const tr=document.createElement('tr');tr.className='mp-total';const t=cell('Total');t.colSpan=4;tr.append(t,cell(money(result.total),'num'),cell(''),cell(''));foot.appendChild(tr);
    }
    async function load(){
        const ticket=++seq;
        const q=new URLSearchParams({month:$('exMonth').value,categoryId:$('exFilterCategory').value,search:$('exSearch').value,includeVoid:$('exVoid').checked});
        try{const result=await request('/Accounts/GetExpenses?'+q);if(ticket===seq){render(result);}}catch(e){$('exMessage').hidden=false;$('exMessage').textContent=e.message;$('exMessage').classList.add('error');}
    }
    async function voidExpense(row){
        if(!await confirmDialog('Void this expense of '+money(row.amount)+' ('+row.description+') and cancel voucher '+row.voucherNo+'?'))return;
        await run(async()=>{const r=await request('/Accounts/VoidExpense',{expenseEntryId:row.expenseEntryId,rowVersion:row.rowVersion});message(r.message);});
        await load();
    }
    function openModal(){
        $('exForm').reset();$('exDate').value=iso(new Date());$('exModalMessage').hidden=true;$('exMessage').hidden=true;
        modal.showModal();$('exCategory').focus();
    }
    async function save(keepOpen){
        if(!$('exForm').reportValidity())return;
        await run(async()=>{
            const r=await request('/Accounts/SaveExpense',{expenseDate:$('exDate').value,accountHeadId:Number($('exCategory').value),description:$('exDescription').value,amount:Number($('exAmount').value)});
            if(keepOpen){$('exDescription').value='';$('exAmount').value='';message(r.message);$('exDescription').focus();}
            else{modal.close();message(r.message);}
        });
        await load();
    }
    $('exNew').addEventListener('click',openModal);
    modal.querySelectorAll('[data-close]').forEach(b=>b.addEventListener('click',()=>modal.close()));
    $('exForm').addEventListener('submit',e=>{e.preventDefault();save(false);});
    $('exSaveNew').addEventListener('click',()=>save(true));
    for(const id of ['exMonth','exFilterCategory','exVoid'])$(id).addEventListener('change',load);
    let timer;$('exSearch').addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(load,300);});
    (async()=>{
        $('exDate').max=iso(new Date());$('exMonth').value=iso(new Date()).slice(0,7);
        try{
            const cats=(await request('/Accounts/GetExpenseCategories')).data;
            for(const c of cats){$('exCategory').add(new Option(c.accountName,c.accountHeadId));$('exFilterCategory').add(new Option(c.accountName,c.accountHeadId));}
        }catch(e){message(e.message,true);}
        load();
    })();
})();
