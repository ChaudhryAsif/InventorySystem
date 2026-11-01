(() => {
    'use strict';
    const page=document.querySelector('[data-payroll]');if(!page)return;
    const $=id=>document.getElementById(id), form=$('prForm'), modal=$('prModal'), slips=page.dataset.payroll==='slips';
    let employees=[], rows=[], busy=false, confirming=false, currentStatus='Draft', dirty=false, queryRevision=0, returnFocus=null;
    const today=()=>{const d=new Date();return d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0');};
    const money=n=>Number(n||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
    function message(text,error=false){const target=modal.open?$('prModalMessage'):$('prMessage');target.hidden=false;target.textContent=text;target.classList.toggle('error',error);}
    async function request(url,body){
        const response=await fetch(url,body===undefined?{headers:{Accept:'application/json'}}:{method:'POST',headers:{'Content-Type':'application/json',Accept:'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||'','X-Request-ID':crypto.randomUUID()},body:JSON.stringify(body)});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success)throw Error(result?.message||'Unable to complete the request. Reload to check the saved result.');
        return result;
    }
    function net(){if(slips)$('netSalary').textContent=money(Number($('basicSalary').value)+Number($('allowances').value)-Number($('deductions').value));}
    function controls(){
        form.querySelectorAll('input:not([type=hidden]),select,textarea').forEach(el=>el.disabled=busy||(slips&&currentStatus!=='Draft'));
        page.querySelectorAll('button').forEach(el=>el.disabled=busy||confirming);
        modal.setAttribute('aria-busy',String(busy));
        if(slips){modal.querySelector('[data-close-footer]').textContent=currentStatus==='Draft'?'Cancel':'Close';$('prSave').hidden=currentStatus!=='Draft';$('prIssue').hidden=currentStatus!=='Draft'||!Number($('salarySlipId').value);$('prIssue').disabled=busy||confirming||dirty;}
    }
    async function action(fn){if(busy||confirming)return;busy=true;controls();try{await fn();}catch(e){message(e.message,true);}finally{busy=false;controls();if(!modal.open&&returnFocus)(returnFocus.isConnected?returnFocus:$('prNew')).focus();}}
    function reset(){
        form.reset();$('rowVersion').value='';$('prModalMessage').hidden=true;dirty=false;currentStatus='Draft';
        if(slips){
            $('employeeId').querySelectorAll('[data-saved-only]').forEach(option=>option.remove());
            $('salarySlipId').value='0';$('period').value=today().slice(0,7);$('slipDate').value=today();$('prPrint').hidden=true;
        } else {$('employeeId').value='0';$('joiningDate').value=today();}
        $('prHeading').textContent=slips?'New salary slip':'Add employee';controls();net();
    }
    function showModal(trigger){returnFocus=trigger||document.activeElement;$('prMessage').hidden=true;modal.showModal();(slips&&currentStatus!=='Draft'?modal.querySelector('[data-close-modal]'):$(slips?'employeeId':'employeeCode')).focus();}
    function closeModal(){dirty=false;modal.close();(returnFocus?.isConnected?returnFocus:$('prNew')).focus();}
    // The shared confirmation panel must be above the native dialog's top layer.
    async function confirmPopup(text){
        if(confirming)return false;
        confirming=true;const wasOpen=modal.open, focused=document.activeElement;
        if(wasOpen)modal.close();controls();
        try{return await confirmDialog(text);}
        finally{confirming=false;controls();if(wasOpen){modal.showModal();if(focused?.isConnected)focused.focus();}}
    }
    async function canLeave(){return !dirty||await confirmPopup('Discard unsaved changes?');}
    async function requestClose(){if(busy||confirming)return;if(await canLeave())closeModal();}
    page.querySelectorAll('[data-close-modal]').forEach(button=>button.addEventListener('click',requestClose));
    modal.addEventListener('cancel',event=>{event.preventDefault();requestClose();});
    modal.addEventListener('keydown',event=>{
        if(event.key!=='Tab')return;
        const focusable=[...modal.querySelectorAll('button,input:not([type=hidden]),select,textarea,a[href],[tabindex]')].filter(el=>!el.disabled&&el.tabIndex>=0&&el.getClientRects().length);
        const first=focusable[0],last=focusable[focusable.length-1];
        if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
        else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
    });
    modal.addEventListener('click',event=>{const bounds=modal.getBoundingClientRect();if(event.target===modal&&(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom))requestClose();});
    $('prNew').addEventListener('click',async event=>{if(!busy&&!confirming&&await canLeave()){reset();showModal(event.currentTarget);}});
    form.addEventListener('input',()=>{dirty=true;net();controls();});
    form.addEventListener('change',()=>{dirty=true;controls();});
    function cell(tr,text){const td=document.createElement('td');td.textContent=text;tr.append(td);return td;}
    function button(container,text,handler){const button=document.createElement('button');button.type='button';button.textContent=text;button.disabled=busy||confirming;button.addEventListener('click',handler);container.append(button);return button;}
    function render(){
        const tbody=$('prRows');tbody.replaceChildren();
        const search=slips?'':$('prSearch').value.toLowerCase(),status=$('prStatus').value;
        const visible=rows.filter(row=>slips?(!status||row.status===status):((row.fullName+' '+row.employeeCode).toLowerCase().includes(search)&&(!status||row.isActive===(status==='active'))));
        for(const row of visible){const tr=document.createElement('tr');
            if(slips){cell(tr,'SS-'+String(row.salarySlipId).padStart(6,'0'));cell(tr,row.period.slice(0,7));cell(tr,row.employeeName);cell(tr,money(row.netSalary));cell(tr,row.status);}
            else{cell(tr,row.employeeCode);cell(tr,row.fullName);cell(tr,row.designation);cell(tr,money(row.basicSalary));cell(tr,row.isActive?'Active':'Inactive');}
            const actions=document.createElement('div');actions.className='pr-row-actions';cell(tr,'').append(actions);
            button(actions,slips?(row.status==='Draft'?'Edit':'View'):'Edit',async event=>{if(!busy&&!confirming&&await canLeave())open(row,event.currentTarget);});
            if(!slips||row.status==='Draft')button(actions,'Delete',()=>deletePayroll(row)).classList.add('pr-danger');
            if(slips){
                if(row.status==='Draft')button(actions,'Issue',()=>issue(row));
                const print=document.createElement('a');print.className='pr-link pr-secondary';print.textContent='Print';print.href='/Accounts/PrintSalarySlip?id='+row.salarySlipId;print.target='_blank';print.rel='noopener';actions.append(print);
            }
            tbody.append(tr);
        }
        if(!visible.length){const tr=document.createElement('tr'),td=cell(tr,'No records found.');td.colSpan=6;tbody.append(tr);}
    }
    function open(row,trigger){
        reset();for(const key of slips?['salarySlipId','employeeId','basicSalary','allowances','deductions','remarks','rowVersion']:['employeeId','employeeCode','fullName','designation','phone','basicSalary','rowVersion'])$(key).value=row[key]??'';
        if(slips){
            if(!$('employeeId').value){const option=new Option(row.employeeName+' (inactive)',row.employeeId);option.dataset.savedOnly='true';$('employeeId').add(option);$('employeeId').value=row.employeeId;}
            $('period').value=row.period.slice(0,7);$('slipDate').value=row.slipDate.slice(0,10);currentStatus=row.status;
            $('prPrint').hidden=false;$('prPrint').href='/Accounts/PrintSalarySlip?id='+row.salarySlipId;
            $('prHeading').textContent=row.status+' salary slip SS-'+String(row.salarySlipId).padStart(6,'0');
        }else{$('joiningDate').value=row.joiningDate.slice(0,10);$('isActive').checked=row.isActive;$('prHeading').textContent='Edit employee';}
        dirty=false;controls();net();showModal(trigger);
    }
    async function loadRows(){
        const revision=++queryRevision;
        const query=slips?'?employeeId='+encodeURIComponent($('prEmployeeFilter').value)+'&year='+encodeURIComponent($('prYear').value)+'&month='+encodeURIComponent($('prMonth').value):'';
        const data=await request(slips?'/Accounts/GetSalarySlips'+query:'/Accounts/GetEmployees');
        if(revision===queryRevision){rows=data.data;render();}
    }
    async function finishSave(text){
        closeModal();message(text);
        try{await loadRows();}catch{message(text+' The list could not refresh. Reload to see the latest records.',true);}
    }
    form.addEventListener('submit',event=>{event.preventDefault();action(async()=>{
        let body;
        if(slips)body={salarySlipId:Number($('salarySlipId').value),employeeId:Number($('employeeId').value),period:$('period').value+'-01',slipDate:$('slipDate').value,basicSalary:Number($('basicSalary').value),allowances:Number($('allowances').value),deductions:Number($('deductions').value),remarks:$('remarks').value,rowVersion:$('rowVersion').value};
        else body={employeeId:Number($('employeeId').value),employeeCode:$('employeeCode').value,fullName:$('fullName').value,designation:$('designation').value,phone:$('phone').value,joiningDate:$('joiningDate').value,basicSalary:Number($('basicSalary').value),isActive:$('isActive').checked,rowVersion:$('rowVersion').value};
        const result=await request('/Accounts/'+(slips?'SaveSalarySlip':'SaveEmployee'),body);
        await finishSave(result.message);
    });});
    async function deletePayroll(row){
        if(busy||confirming||dirty)return;
        const name=slips?'draft salary slip SS-'+String(row.salarySlipId).padStart(6,'0'):'employee '+row.fullName;
        if(!await confirmPopup('Delete '+name+'? This cannot be undone.'))return;
        action(async()=>{
            const body=slips?{salarySlipId:row.salarySlipId,rowVersion:row.rowVersion}:{employeeId:row.employeeId,rowVersion:row.rowVersion};
            const result=await request('/Accounts/'+(slips?'DeleteSalarySlip':'DeleteEmployee'),body);
            await finishSave(result.message);
        });
    }
    async function issue(row){
        if(busy||confirming||dirty)return;
        if(!await confirmPopup('Issue this salary slip? Issued slips cannot be edited.'))return;
        action(async()=>{const result=await request('/Accounts/UpdateSalarySlipStatus',{salarySlipId:row.salarySlipId,rowVersion:row.rowVersion});await finishSave(result.message);});
    }
    if(slips){
        $('employeeId').addEventListener('change',()=>{const employee=employees.find(x=>x.employeeId===Number($('employeeId').value));if(employee){$('basicSalary').value=employee.basicSalary;net();}});
        $('prIssue').addEventListener('click',()=>issue({salarySlipId:Number($('salarySlipId').value),rowVersion:$('rowVersion').value}));
        for(const id of ['prEmployeeFilter','prYear','prMonth'])$(id).addEventListener('change',()=>loadRows().catch(e=>message(e.message,true)));
    }else $('prSearch').addEventListener('input',render);
    $('prStatus').addEventListener('change',render);
    window.addEventListener('beforeunload',event=>{if(dirty){event.preventDefault();event.returnValue='';}});
    action(async()=>{
        if(slips){employees=(await request('/Accounts/GetEmployees')).data;for(const employee of employees){if(employee.isActive)$('employeeId').add(new Option(employee.employeeCode+' — '+employee.fullName,employee.employeeId));$('prEmployeeFilter').add(new Option(employee.employeeCode+' — '+employee.fullName,employee.employeeId));}$('prYear').value=today().slice(0,4);}
        reset();await loadRows();
    });
})();
