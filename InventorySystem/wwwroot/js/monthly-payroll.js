(() => {
    'use strict';
    const page=document.getElementById('mpPage');if(!page)return;
    const $=id=>document.getElementById(id);
    let rows=[], state={id:0,status:'New',rowVersion:null,voucherNo:'',slipCount:0}, busy=false, dirty=false, seq=0;
    const r2=n=>Math.round((Number(n)+Number.EPSILON)*100)/100;
    const money=n=>Number(n||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
    const perDay=r=>r.salary/30, perHour=r=>r.salary/30/8;
    const salary=r=>r2(perDay(r)*r.attendanceDays), total=r=>r2(salary(r)+r.overtimePay), payable=r=>r2(total(r)-r.advance);
    const defaultOt=r=>r2(perHour(r)*r.overtimeHours);
    const locked=()=>state.status==='Posted';
    function message(text,error=false){const m=$('mpMessage');m.hidden=!text;m.textContent=text||'';m.classList.toggle('error',error);}
    async function request(url,body){
        const response=await fetch(url,body===undefined?{headers:{Accept:'application/json'}}:{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||'','X-Request-ID':crypto.randomUUID()},body:JSON.stringify(body)});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success)throw Error(result?.message||'Unable to complete the request. Reload to check the saved result.');
        return result;
    }
    function cell(text,cls){const td=document.createElement('td');td.textContent=text;if(cls)td.className=cls;return td;}
    function input(r,field,max,step){
        const td=document.createElement('td'), el=document.createElement('input');
        el.type='number';el.min='0';el.max=String(max);el.step=step;el.value=r[field]||'';el.placeholder='0';el.disabled=busy||locked();
        el.setAttribute('aria-label',r.name+' '+field);el.dataset.field=field;
        el.addEventListener('input',()=>{
            r[field]=Number(el.value)||0;dirty=true;
            if(field==='overtimeHours'&&!r.otManual){r.overtimePay=defaultOt(r);}
            if(field==='overtimePay')r.otManual=true;
            refreshRow(r);controls();
        });
        td.appendChild(el);return td;
    }
    function build(){
        const body=$('mpRows');body.replaceChildren();
        rows.forEach((r,i)=>{
            const tr=document.createElement('tr');tr.dataset.id=r.employeeId;
            tr.append(cell(i+1),cell(r.name),cell(r.designation),cell(money(r.salary),'num'),cell('','num pd'),cell('','num ph'),
                input(r,'attendanceDays',31,'0.5'),input(r,'overtimeHours',744,'0.5'),cell('','num sal'),input(r,'overtimePay',1000000000,'0.01'),cell('','num tot'),input(r,'advance',1000000000,'0.01'),cell('','num pay strong'));
            body.appendChild(tr);r.tr=tr;refreshRow(r,true);
        });
        totals();
    }
    function refreshRow(r,skipTotals){
        const tr=r.tr;if(!tr)return;
        tr.querySelector('.pd').textContent=money(perDay(r));tr.querySelector('.ph').textContent=money(perHour(r));
        tr.querySelector('.sal').textContent=money(salary(r));tr.querySelector('.tot').textContent=money(total(r));
        const p=tr.querySelector('.pay');p.textContent=money(payable(r));p.classList.toggle('mp-bad',payable(r)<0);
        const ot=tr.querySelector('[data-field=overtimePay]');if(document.activeElement!==ot)ot.value=r.overtimePay||'';
        if(!skipTotals)totals();
    }
    function totals(){
        const sum=f=>rows.reduce((a,r)=>a+f(r),0), foot=$('mpFoot');foot.replaceChildren();
        const tr=document.createElement('tr');tr.className='mp-total';
        const blank=n=>Array.from({length:n},()=>document.createElement('td'));
        tr.append(cell(''),cell('Total'),...blank(1),cell(money(sum(r=>r.salary)),'num'),...blank(4),cell(money(sum(salary)),'num'),cell(money(sum(r=>r.overtimePay)),'num'),cell(money(sum(total)),'num'),cell(money(sum(r=>r.advance)),'num'),cell(money(sum(payable)),'num'));
        foot.appendChild(tr);
    }
    function controls(){
        $('mpStatus').textContent=state.status==='New'?'Not saved':state.status;
        $('mpVoucher').textContent=state.voucherNo?'Voucher '+state.voucherNo:(dirty?'Unsaved changes':'');
        $('mpSave').hidden=locked();$('mpPost').hidden=locked();$('mpVoid').hidden=!locked();
        $('mpSave').disabled=busy||!rows.length||!dirty;
        $('mpPost').disabled=busy||!state.id||dirty||!rows.length;
        $('mpSlips').hidden=!locked()||state.slipCount>0;$('mpSlipsLink').hidden=!locked()||!state.slipCount;
        $('mpSlipsLink').textContent='View salary slips ('+state.slipCount+')';
        $('mpSlips').disabled=busy;$('mpVoid').disabled=busy;$('mpPeriod').disabled=busy;
        page.querySelectorAll('#mpRows input').forEach(el=>el.disabled=busy||locked());
    }
    async function run(fn){if(busy)return;busy=true;controls();try{await fn();}catch(e){message(e.message,true);}finally{busy=false;controls();}}
    async function load(){
        const period=$('mpPeriod').value;if(!period)return;
        const ticket=++seq;message('');
        await run(async()=>{
            const result=await request('/Accounts/GetPayrollMonth?period='+encodeURIComponent(period));
            if(ticket!==seq)return;
            state={id:result.payrollMonthId,status:result.status,rowVersion:result.rowVersion,voucherNo:result.voucherNo,slipCount:result.slipCount||0};
            rows=result.rows.map(r=>{const x={...r};x.otManual=x.overtimePay!==defaultOt(x);return x;});
            dirty=false;build();
            if(!rows.length)message('No active employees. Add employees in Employee Data first.',true);
        });
    }
    function payload(){return {payrollMonthId:state.id,period:$('mpPeriod').value,rowVersion:state.rowVersion,
        lines:rows.map(r=>({employeeId:r.employeeId,attendanceDays:r.attendanceDays||0,overtimeHours:r.overtimeHours||0,overtimePay:r.overtimePay||0,advance:r.advance||0}))};}
    function validate(){
        const bad=rows.find(r=>r.advance>total(r));
        if(bad){message(bad.name+': advance cannot exceed the total salary.',true);return false;}
        return true;
    }
    async function save(){
        if(!validate())return;
        await run(async()=>{const r=await request('/Accounts/SavePayrollMonth',payload());state.id=r.payrollMonthId;state.rowVersion=r.rowVersion;state.status='Draft';dirty=false;message(r.message);});
    }
    async function post(){
        if(!await confirmDialog('Post this month and record '+money(rows.reduce((a,r)=>a+payable(r),0))+' as a cash payment?'))return;
        await run(async()=>{const r=await request('/Accounts/PostPayrollMonth',{payrollMonthId:state.id,rowVersion:state.rowVersion});message(r.message);});
        await load();
    }
    async function createSlips(){
        if(!await confirmDialog('Create an issued salary slip for each worker from this posted month?'))return;
        await run(async()=>{const r=await request('/Accounts/CreatePayrollSlips',{payrollMonthId:state.id,rowVersion:state.rowVersion});message(r.message);});
        await load();
    }
    async function voidMonth(){
        if(!await confirmDialog('Void the cash payment voucher, remove the salary slips made from it, and reopen this month as a draft?'))return;
        await run(async()=>{const r=await request('/Accounts/VoidPayrollMonth',{payrollMonthId:state.id,rowVersion:state.rowVersion});message(r.message);});
        await load();
    }
    const now=new Date();$('mpPeriod').value=now.getFullYear()+'-'+String(now.getMonth()+1).padStart(2,'0');
    let shownPeriod=$('mpPeriod').value;
    $('mpPeriod').addEventListener('change',async()=>{
        if(dirty&&!await confirmDialog('Discard unsaved changes for this month?')){$('mpPeriod').value=shownPeriod;return;}
        shownPeriod=$('mpPeriod').value;load();
    });
    $('mpSlips').addEventListener('click',createSlips);$('mpSave').addEventListener('click',save);$('mpPost').addEventListener('click',post);$('mpVoid').addEventListener('click',voidMonth);
    $('mpPrint').addEventListener('click',()=>PrintTab.open());
    window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
    load();
})();
