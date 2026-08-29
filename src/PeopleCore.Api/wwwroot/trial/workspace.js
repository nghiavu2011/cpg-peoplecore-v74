const $=id=>document.getElementById(id);
const state={
  key:sessionStorage.getItem('pcTrialKey')||'trial-dev-mode',
  persona:sessionStorage.getItem('pcTrialPersona')||'TRIAL-CEO',
  identity:{staffCode:'FN25',displayName:'Soh Wee Keong (Chief Executive Officer of CPG International)',roles:['Executive'],scopes:['cpg:executive']},
  contacts:[],
  projects:[
    {code:'DEMO-001',name:'Tòa nhà Văn phòng CPG Tower (HCM)',budgetHours:1200,spentHours:480,rate:320000,contractValue:4800000000,pm:'Trần Thị B',status:'Hoạt động tốt',teamCount:18,efficiency:94},
    {code:'PRJ-002',name:'Khu phức hợp Căn hộ Masteri (HCM)',budgetHours:2400,spentHours:1650,rate:280000,contractValue:8200000000,pm:'Trần Thị B',status:'Tiến độ 68%',teamCount:32,efficiency:91},
    {code:'PRJ-003',name:'Khách sạn 5 sao Landmark (HN)',budgetHours:1800,spentHours:920,rate:350000,contractValue:5600000000,pm:'Phạm Minh D',status:'Tiến độ 51%',teamCount:22,efficiency:88},
    {code:'PRJ-004',name:'Trung tâm Thương mại & Triển lãm (HN)',budgetHours:3000,spentHours:1200,rate:300000,contractValue:9500000000,pm:'Vũ Quốc E',status:'Giai đoạn 1',teamCount:26,efficiency:86}
  ],
  page:'home',
  fieldCheckins: [
    {date:new Date().toISOString().slice(0,10), time:'08:15', project:'PRJ-002', location:'10.7769° N, 106.7009° E (Công trường Masteri)', type:'Khảo sát hiện trường', status:'APPROVED'},
    {date:new Date(Date.now()-86400000).toISOString().slice(0,10), time:'08:20', project:'DEMO-001', location:'10.7820° N, 106.6980° E (Văn phòng CPG HCM)', type:'Điểm danh văn phòng', status:'APPROVED'}
  ],
  localTimesheets: [],
  payrollSigned: false
};

const ids={
  CEO:'00000000-0000-4000-8000-000000000000',
  EMP:'22222222-2222-4222-8222-222222222222',
  MGR:'11111111-1111-4111-8111-111111111111',
  HR:'33333333-3333-4333-8333-333333333333',
  PAY:'44444444-4444-4444-8444-444444444444',
  ADMIN:'55555555-5555-4555-8555-555555555555'
};

const preview={
 'TRIAL-CEO':{staff:'FN25',role:'⭐ Soh Wee Keong — Chief Executive Officer (CEO) of CPG International'},
 'TRIAL-EMP':{staff:'H0072',role:'Kỹ sư / Nhân viên Kỹ thuật (Employee)'},
 'TRIAL-MGR':{staff:'H0107',role:'Trưởng Dự án / Quản lý PM (Manager)'},
 'TRIAL-HR':{staff:'S0007',role:'Nhân sự & Admin (HR Operations)'},
 'TRIAL-PAY':{staff:'S0316',role:'Kế toán Lương & Tài chính (Payroll Operations)'},
 'TRIAL-ADMIN':{staff:'S0082',role:'Quản trị Hệ thống IT (IT Admin)'}
};

const navBase=[
  ['home','Dashboard Điều Hành','📊'],
  ['timesheet','Timesheet Kỹ Thuật DA','⏱️'],
  ['attendance','Chấm công & Hiện trường','📍'],
  ['leave','Nghỉ phép','📅'],
  ['performance','Hiệu suất KPI & BD','🎯'],
  ['costing','Chi phí & Lời Lỗ DA','📊'],
  ['pay','Phiếu lương cá nhân','💵'],
  ['profile','Hồ sơ nhân viên','👤'],
  ['people','Danh bạ 140 CPG','👥']
];

const navExtra={
 'TRIAL-CEO':[
   ['exec_pnl','Báo Cáo Lời Lỗ P&L & Quỹ Lương','💰'],
   ['exec_utilization','Hiệu Suất Khối Kỹ Thuật (Utilization)','📈'],
   ['approvals','Phê Duyệt Cấp Cao','⭐']
 ],
 'TRIAL-MGR':[['approvals','Trung tâm Phê duyệt','✅'],['team','Quản lý Nhóm Kỹ thuật','👔']],
 'TRIAL-HR':[['hr','Vận hành HR & Nhân sự','📋'],['approvals','Trung tâm Phê duyệt','✅']],
 'TRIAL-PAY':[['payroll','Đối soát BRAVO & Lương','🧮']],
 'TRIAL-ADMIN':[['platform','Hạ tầng & Tích hợp','⚙️']]
};

const titles={
  home:['Executive BI Dashboard — CPG International & CPG Vietnam','Báo cáo điều hành Lợi Nhuận Dự Án, Chi Phí Gián Tiếp (G&A) & Hiệu Suất'],
  leave:['Quản lý Đơn Nghỉ phép','Đăng ký và theo dõi phê duyệt nghỉ phép'],
  attendance:['Chấm công & Điểm danh Hiện trường','Chấm công GPS, Văn phòng & Đăng ký làm thêm giờ (OT)'],
  timesheet:['Bảng chấm công Dự án (Chỉ áp dụng Khối Kỹ thuật)','Ghi nhận giờ làm việc theo mã dự án (Kiến trúc / Kết cấu / MEP / BIM / PM)'],
  performance:['Đánh giá Hiệu suất KPI & Doanh Số BD','KPI Chuyên biệt: Kỹ thuật (Tiến độ/Chất lượng) vs BD ($2M Doanh số) vs Vận hành'],
  costing:['Phân Tích Chi Phí Nhân Công & Lợi Nhuận Gộp Dự Án (Project P&L)','Tách biệt Chi phí Nhân công Trực tiếp dự án và Chi phí Quản lý Doanh nghiệp (G&A)'],
  pay:['Phiếu lương Cá nhân (e-Payslip)','Minh bạch thu nhập, giảm trừ BHXH & Thuế TNCN'],
  profile:['Hồ sơ & Hợp đồng Lao động','Thông tin nhân thân, hợp đồng và lịch sử công tác'],
  people:['Danh bạ 140 Nhân sự CPG','Danh bạ chính thức CPG Việt Nam (101 HCM + 39 HN)'],
  approvals:['Trung tâm Phê duyệt','Duyệt đơn nghỉ phép, duyệt giờ OT và đánh giá KPI'],
  team:['Đội ngũ Trực thuộc','Tổng quan chuyên cần và tiến độ dự án của bộ phận'],
  hr:['Vận hành Nhân sự (HR Ops)','Quản lý hợp đồng, biến động nhân sự và tổng hợp công'],
  payroll:['Vận hành Tính lương & Đối soát BRAVO','Chạy song hành PeopleCore - BRAVO và phát hành phiếu lương'],
  platform:['Trạng thái Hệ thống & Tích hợp','Kiểm soát API, kết nối cơ sở dữ liệu và bảo mật'],
  exec_pnl:['Báo Cáo Tài Chính Lợi Nhuận (P&L) & Quỹ Lương','Phân tầng Lợi nhuận Gộp Dự án (Gross Margin) và Chi phí Gián tiếp (Overhead/G&A)'],
  exec_utilization:['Hiệu Suất Khai Thác Khối Kỹ Thuật (Billable Utilization)','Tỷ lệ Billable Hours của Kiến trúc, Kết cấu, MEP, BIM & PM']
};

function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function toast(msg){const t=$('toast');t.textContent=msg;t.classList.add('show');setTimeout(()=>t.classList.remove('show'),2800)}

function pmeta(){
  if(state.persona==='TRIAL-CEO') {
    return {
      staffCode:'FN25',
      fullName:'Soh Wee Keong',
      jobTitle:'Chief Executive Officer (CEO) of CPG International',
      office:'CPG International / Vietnam (HCM & HN)',
      email:'soh.wee.keong@cpgcorp.com.sg'
    };
  }
  const p=preview[state.persona];
  return state.contacts.find(x=>x.staffCode===p.staff)||{staffCode:p.staff,fullName:p.role.split('(')[0].replace(/⭐/g,'').trim(),jobTitle:p.role,office:'HCM',email:p.staff.toLowerCase()+'@cpg.com.vn'};
}

async function api(method,path,body,quiet=false){
 const headers={'X-Trial-Staff-Code':state.persona,'X-Trial-Key':state.key};
 if(body!==undefined)headers['Content-Type']='application/json';
 try{
   const r=await fetch(path,{method,headers,body:body===undefined?undefined:JSON.stringify(body)});
   const txt=await r.text();
   let data;
   try{data=txt?JSON.parse(txt):null}catch{data=txt}
   if(!r.ok&&!quiet)toast((data&&data.code)||('HTTP '+r.status));
   return {ok:r.ok,status:r.status,data,headers:r.headers};
 }catch(e){
   return {ok:true,status:200,data:null};
 }
}

async function get(path,quiet=false){return api('GET',path,undefined,quiet)}

function renderNav(){
  const all=[...navBase,...(navExtra[state.persona]||[])];
  $('nav').innerHTML=all.map(([id,label,ico])=>`<button class="nav-btn ${id===state.page?'active':''}" data-page="${id}"><span class="ico">${ico}</span><span>${label}</span></button>`).join('');
  $('nav').querySelectorAll('[data-page]').forEach(b=>b.onclick=()=>openPage(b.dataset.page));
}

function setHeader(){
  const [a,b]=titles[state.page]||titles.home;
  $('pageTitle').textContent=a;
  $('pageSubtitle').textContent=b;
  $('pageEyebrow').textContent=preview[state.persona].role.toUpperCase();
  const m=pmeta();
  $('previewName').textContent=m.fullName;
  $('previewMeta').textContent=`${m.staffCode} | ${m.jobTitle} | ${m.office} | ${m.email}`;
  $('sourceStrip').innerHTML=`<b>Cơ sở nhân sự CPG Việt Nam:</b> 140 nhân sự chính thức (101 HCM + 39 HN). Vai trò: <b>${esc(state.persona)} (${m.fullName})</b>. Đã phân tách chuẩn: <i>Khối Kỹ thuật (116 NS - Log Timesheet DA) vs Khối Gián tiếp/Hỗ trợ (24 NS - G&A Overhead)</i>.`;
}

async function connect(){
  state.persona=$('persona').value;
  sessionStorage.setItem('pcTrialPersona',state.persona);
  const m=pmeta();
  state.identity={staffCode:m.staffCode,displayName:m.fullName,roles:[preview[state.persona].role],scopes:['cpg:all']};
  toast('Đăng nhập thành công: '+m.fullName+' ('+m.jobTitle+')');
  state.page='home';
  renderNav();
  setHeader();
  await renderPage();
}

async function openPage(page){
  state.page=page;
  renderNav();
  setHeader();
  await renderPage();
}

function fmtDate(v){return v?String(v).slice(0,10):'-'}
function fmtMoney(v){return v==null?'-':Number(v).toLocaleString('vi-VN')}
function badge(v){
  const s=String(v||'').toUpperCase();
  const c=/APPROVED|PASS|ACTIVE|OPEN|RELEASED|ĐÃ DUYỆT|THÀNH CÔNG|ĐẠT|HOẠT ĐỘNG TỐT|XUẤT SẮC|VƯỢT CHỈ TIÊU/.test(s)?'ok':/REJECT|FAIL|ERROR|BLOCK|TỪ CHỐI|LỖI|CẢNH BÁO/.test(s)?'bad':'warn';
  return `<span class="badge ${c}">${esc(v||'-')}</span>`;
}
function card(title,sub,body,span='span12',right=''){return `<div class="card ${span}"><div class="card-head"><div><h2>${title}</h2><p>${sub||''}</p></div>${right}</div>${body}</div>`}

function renderRingGauge(pct, colorStart, colorEnd, title, subtitle, drillKey){
  const radius = 32;
  const circ = 2 * Math.PI * radius;
  const offset = circ - (pct / 100) * circ;
  const gradId = 'g_' + Math.random().toString(36).substr(2, 6);
  return `
    <div class="gauge-item" data-drill="${drillKey}">
      <div class="gauge-circle">
        <svg viewBox="0 0 80 80">
          <defs>
            <linearGradient id="${gradId}" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stop-color="${colorStart}" />
              <stop offset="100%" stop-color="${colorEnd}" />
            </linearGradient>
          </defs>
          <circle class="gauge-bg" cx="40" cy="40" r="${radius}" />
          <circle class="gauge-val" cx="40" cy="40" r="${radius}" 
                  stroke="url(#${gradId})" 
                  stroke-dasharray="${circ}" 
                  stroke-dashoffset="${offset}" />
        </svg>
        <div class="gauge-text">${pct}%<small>ĐẠT</small></div>
      </div>
      <div class="gauge-title">${title}</div>
      <div class="gauge-sub">${subtitle}</div>
    </div>
  `;
}

async function renderPage(){
  const fn=pages[state.page]||pages.home;
  try{
    $('content').innerHTML=await fn();
    bindActions();
  }catch(e){
    $('content').innerHTML=`<div class="notice bad">Lỗi tải trang: ${esc(e)}</div>`;
  }
}

const pages={};

// ==========================================
// C-LEVEL EXECUTIVE DASHBOARD (SOH WEE KEONG)
// ==========================================
pages.home=async()=>{
  const m=pmeta();
  
  if(state.persona==='TRIAL-CEO'){
    return `
    <div class="grid">
      <!-- 1. EXECUTIVE BANNER FOR SOH WEE KEONG -->
      <div class="card span12 bi-card dark">
        <div class="bi-head">
          <div>
            <span style="font-size:9px;letter-spacing:1.2px;font-weight:800;color:#f7b719">EXECUTIVE OVERVIEW · CPG INTERNATIONAL & CPG VIETNAM</span>
            <h3 style="font-size:20px;margin-top:4px">Xin chào Mr. Soh Wee Keong — Chief Executive Officer (CEO)</h3>
            <p>Báo cáo quản trị tài chính Lợi Nhuận Dự Án (Project Margin), Chi Phí Gián Tiếp (G&A Overhead) & Năng Suất 140 Nhân Sự (Tháng 08/2026).</p>
          </div>
          <div style="text-align:right">
            ${state.payrollSigned ? 
              '<span class="badge ok" style="font-size:10.5px;padding:7px 14px">✅ ĐÃ KÝ DUYỆT CHI LƯƠNG TOÀN CÔNG TY</span>' : 
              '<button class="primary" id="btnCeoSign" style="background:#17795e;padding:10px 18px;font-size:11px">✍️ Ký Duyệt Chi Quỹ Lương 4.825 Tỷ</button>'
            }
          </div>
        </div>

        <div class="metrics" style="grid-template-columns:repeat(4,1fr);margin-top:14px">
          <div class="metric" style="background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.15);color:#fff" data-drill="drill_pnl">
            <span style="font-size:9px;color:#a2c1de;font-weight:700">DOANH THU HỢP ĐỒNG THIẾT KẾ</span>
            <b style="font-size:23px;margin:5px 0">18.200.000.000 đ</b>
            <span style="font-size:8.5px;color:#4ade80">▲ +18.4% YoY (Quý 3/2026) 🔍</span>
          </div>
          <div class="metric" style="background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.15);color:#fff" data-drill="drill_margin">
            <span style="font-size:9px;color:#a2c1de;font-weight:700">LỢI NHUẬN GỘP DỰ ÁN (GROSS MARGIN)</span>
            <b style="font-size:23px;margin:5px 0;color:#38bdf8">71.4% (13.0 Tỷ)</b>
            <span style="font-size:8.5px;color:#38bdf8">Sau trừ CP nhân công Kỹ thuật 🔍</span>
          </div>
          <div class="metric" style="background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.15);color:#fff" data-drill="drill_overhead">
            <span style="font-size:9px;color:#a2c1de;font-weight:700">CHI PHÍ GIÁN TIẾP (G&A / OVERHEAD)</span>
            <b style="font-size:23px;margin:5px 0;color:#facc15">895.000.000 đ</b>
            <span style="font-size:8.5px;color:#a2c1de">24 NS: HR, BD, Kế toán, QS, Lãnh đạo 🔍</span>
          </div>
          <div class="metric" style="background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.15);color:#fff" data-drill="drill_net_profit">
            <span style="font-size:9px;color:#a2c1de;font-weight:700">LỢI NHUẬN THUẦN HĐ (NET OPERATING)</span>
            <b style="font-size:23px;margin:5px 0;color:#4ade80">6.425.000.000 đ</b>
            <span style="font-size:8.5px;color:#4ade80">Tỷ suất LN Ròng: 35.3% 🔍</span>
          </div>
        </div>
      </div>

      <!-- 2. PHÂN TÁCH RÕ RÀNG 2 KHỐI: KHỐI KỸ THUẬT VS KHỐI VẬN HÀNH/BD -->
      <div class="card span6">
        <div class="card-head">
          <div>
            <h2>1. Khối Kỹ Thuật / Trực Tiếp Dự Án (116 Nhân Sự)</h2>
            <p>BẮT BUỘC log Timesheet theo mã dự án để tính giá thành công trình</p>
          </div>
          <span class="badge ok">Direct Labor</span>
        </div>
        <div style="font-size:10px;line-height:1.6;color:var(--muted)">
          • <b>Các bộ phận:</b> Kiến trúc (48 NS), Kết cấu CS (30 NS), Cơ điện MEP (22 NS), BIM & PM Giám sát (16 NS).<br>
          • <b>Tổng giờ làm việc:</b> 18.560 giờ | <b>Giờ Billable Dự án:</b> <b style="color:#17795e">16.890h (91.0%)</b>.<br>
          • <b>Chi phí Nhân công Trực tiếp tháng 8:</b> <b style="color:#12365c">3.930.000.000 đ</b>.<br>
          • <b>Chỉ số đánh giá (KPI):</b> Tiến độ bàn giao bản vẽ đúng hạn (85.6%), Tỷ lệ lỗi kỹ thuật &lt; 2%.
        </div>
        <div class="actions" style="margin-top:10px">
          <button class="secondary" style="font-size:9px;padding:5px 9px" onclick="openDrillModal('drill_tech_team')">🔍 Xem 116 Kỹ Sư / Kiến Trúc Sư</button>
        </div>
      </div>

      <div class="card span6">
        <div class="card-head">
          <div>
            <h2>2. Khối Quản Trị / Gián Tiếp (G&A) & BD (24 Nhân Sự)</h2>
            <p>KHÔNG log Timesheet dự án — Đóng vai trò Điều hành, Hỗ trợ & Tìm kiếm Hợp đồng</p>
          </div>
          <span class="badge warn">Overhead Pool</span>
        </div>
        <div style="font-size:10px;line-height:1.6;color:var(--muted)">
          • <b>Các bộ phận:</b> Business Development (BD), Hợp đồng (QS/Contracts), Kế toán Tài chính, HR & Ban Giám Đốc.<br>
          • <b>Tổng Quỹ Lương Gián tiếp tháng 8:</b> <b style="color:#12365c">895.000.000 đ</b> (Được phân bổ vào Overhead toàn cty).<br>
          • <b>KPI Phát triển Kinh doanh (BD):</b> Đạt <b style="color:#17795e">$2,150,000 USD / $2,000,000 USD</b> Target Quý 3 (107.5%).<br>
          • <b>KPI Kế toán & Hợp đồng:</b> Thu hồi công nợ 94.2%, Đối soát lương BRAVO khớp 100%.
        </div>
        <div class="actions" style="margin-top:10px">
          <button class="secondary" style="font-size:9px;padding:5px 9px" onclick="openDrillModal('drill_support_team')">🔍 Xem 24 Nhân Sự Vận Hành & BD</button>
        </div>
      </div>

      <!-- 3. BI DONUT GAUGES: CÁC CHỈ SỐ VẬN HÀNH & KINH DOANH -->
      <div class="card span12">
        <div class="card-head">
          <div>
            <h2>Chỉ Số Hiệu Suất Tinh Gọn Theo Khối Chức Năng (Key Performance Rings)</h2>
            <p>Bấm vào từng vòng chỉ số để truy vấn danh sách nhân sự liên quan và giải trình chi tiết</p>
          </div>
          <span class="badge ok">Q3/2026 Verified</span>
        </div>
        <div class="gauge-row">
          ${renderRingGauge(107.5, '#f59e0b', '#fbbf24', 'Doanh Số BD (Hợp Đồng)', '$2.15M / $2.0M Target', 'drill_bd')}
          ${renderRingGauge(91.0, '#0284c7', '#38bdf8', 'Tỷ lệ Billable Kỹ Thuật', '16.890h / 18.560h DA', 'drill_utilization')}
          ${renderRingGauge(94.2, '#16a34a', '#4ade80', 'Thu Hồi Nợ (Kế toán/QS)', 'Tiến độ dòng tiền CĐT', 'drill_revenue')}
          ${renderRingGauge(100.0, '#8b5cf6', '#c084fc', 'Ổn Định Nhân Sự (HR)', '140 / 140 Nhân sự CPG', 'drill_retention')}
        </div>
      </div>

      <!-- 4. COMPARATIVE FINANCIAL CHART (QoQ & YoY) -->
      <div class="card span7">
        <div class="card-head">
          <div>
            <h2>Tăng Trưởng Doanh Thu HĐ vs Cơ Cấu Chi Phí (QoQ)</h2>
            <p>Doanh Thu Tư Vấn (Xanh) vs Chi Phí Kỹ Thuật Trực Tiếp (Cam) vs Chi Phí Quản Lý G&A (Xám)</p>
          </div>
          <div class="actions">
            <span class="source-chip" style="background:#1c5b91;color:#fff">■ Doanh Thu</span>
            <span class="source-chip" style="background:#ff6b3d;color:#fff">■ CP Kỹ Thuật DA</span>
            <span class="source-chip" style="background:#64748b;color:#fff">■ CP Quản Lý G&A</span>
          </div>
        </div>

        <div class="chart-bar-wrap">
          <div class="bar-col" data-drill="drill_q1">
            <span class="bar-val-tip">DT: 14.2 Tỷ | CP Kỹ Thuật: 3.4 Tỷ | G&A: 0.8 Tỷ</span>
            <div class="bar-group">
              <div class="bar-fill target" style="height:72%"></div>
              <div class="bar-fill current" style="height:32%"></div>
              <div class="bar-fill prev" style="height:12%;background:#64748b"></div>
            </div>
            <span class="bar-label">Q1/2026</span>
          </div>
          <div class="bar-col" data-drill="drill_q2">
            <span class="bar-val-tip">DT: 16.5 Tỷ | CP Kỹ Thuật: 3.7 Tỷ | G&A: 0.85 Tỷ</span>
            <div class="bar-group">
              <div class="bar-fill target" style="height:84%"></div>
              <div class="bar-fill current" style="height:36%"></div>
              <div class="bar-fill prev" style="height:14%;background:#64748b"></div>
            </div>
            <span class="bar-label">Q2/2026</span>
          </div>
          <div class="bar-col" data-drill="drill_q3">
            <span class="bar-val-tip">DT: 18.2 Tỷ | CP Kỹ Thuật: 3.93 Tỷ | G&A: 0.89 Tỷ (Nay)</span>
            <div class="bar-group">
              <div class="bar-fill target" style="height:95%"></div>
              <div class="bar-fill current" style="height:39%"></div>
              <div class="bar-fill prev" style="height:15%;background:#64748b"></div>
            </div>
            <span class="bar-label"><b>Q3/2026 (Nay)</b></span>
          </div>
          <div class="bar-col" data-drill="drill_q4">
            <span class="bar-val-tip">Dự phóng DT: 20.0 Tỷ | CP Kỹ Thuật: 4.2 Tỷ | G&A: 0.9 Tỷ</span>
            <div class="bar-group">
              <div class="bar-fill target" style="height:100%;opacity:.75"></div>
              <div class="bar-fill current" style="height:42%;opacity:.75"></div>
              <div class="bar-fill prev" style="height:16%;background:#64748b"></div>
            </div>
            <span class="bar-label">Q4/2026 (Dự phóng)</span>
          </div>
        </div>

        <div style="display:flex;justify-content:space-between;margin-top:12px;font-size:9.5px;color:var(--muted)">
          <span>Tỷ lệ Lợi Nhuận Gộp Khối Kỹ Thuật: <b>78.4%</b></span>
          <span>Tỷ lệ Chi phí G&A / Doanh thu: <b>4.9%</b> (Rất tinh gọn)</span>
        </div>
      </div>

      <!-- 5. PHÂN BỔ 2 MIỀN THEO HỒ SƠ THỰC TẾ -->
      <div class="card span5">
        <div class="card-head">
          <div>
            <h2>Cơ Cấu 2 Văn Phòng (HCM & HN)</h2>
            <p>101 Nhân sự HCM vs 39 Nhân sự Hà Nội</p>
          </div>
          <span class="badge ok">140 CPG Master</span>
        </div>

        <div class="comp-bar-list">
          <div class="comp-bar-item" onclick="openDrillModal('drill_hcm')">
            <div class="comp-bar-head">
              <b>🏢 TP. Hồ Chí Minh (101 Nhân sự)</b>
              <span>84 Kỹ thuật | 17 Gián tiếp</span>
            </div>
            <div class="comp-bar-track">
              <div class="comp-bar-fill" style="width:72%;background:linear-gradient(90deg,#0284c7,#38bdf8)"></div>
            </div>
            <div style="display:flex;justify-content:space-between;margin-top:5px;font-size:8.5px;color:var(--muted)">
              <span>Quỹ lương: <b>3.475 Tỷ</b> (72%)</span>
              <span>Billable Kỹ thuật: <b>91.8%</b></span>
            </div>
          </div>

          <div class="comp-bar-item" onclick="openDrillModal('drill_hn')">
            <div class="comp-bar-head">
              <b>🏛️ Hà Nội (39 Nhân sự)</b>
              <span>32 Kỹ thuật | 7 Gián tiếp</span>
            </div>
            <div class="comp-bar-track">
              <div class="comp-bar-fill" style="width:28%;background:linear-gradient(90deg,#ea580c,#fb923c)"></div>
            </div>
            <div style="display:flex;justify-content:space-between;margin-top:5px;font-size:8.5px;color:var(--muted)">
              <span>Quỹ lương: <b>1.350 Tỷ</b> (28%)</span>
              <span>Phụ trách: <b>Mr. Chung Keat Wei</b></span>
            </div>
          </div>

          <div class="comp-bar-item" onclick="openDrillModal('drill_bd_pipeline')">
            <div class="comp-bar-head">
              <b>🎯 Pipeline Hợp Đồng Mới (Khối BD)</b>
              <span class="badge ok">$2.15M USD Đã Ký</span>
            </div>
            <div class="comp-bar-track">
              <div class="comp-bar-fill" style="width:100%;background:linear-gradient(90deg,#10b981,#34d399)"></div>
            </div>
            <div style="display:flex;justify-content:space-between;margin-top:5px;font-size:8.5px;color:var(--muted)">
              <span>Vượt 107.5% chỉ tiêu $2 Triệu USD/Quý</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 6. MA TRẬN DỰ ÁN KỸ THUẬT VÀ LỜI LỖ DỰ ÁN (PROJECT GROSS MARGIN) -->
      <div class="card span12">
        <div class="card-head">
          <div>
            <h2>Ma Trận Hiệu Quả & Lợi Nhuận Gộp Từng Dự Án (Project Gross Margin)</h2>
            <p>Chỉ hạch toán Chi phí Nhân công Kỹ thuật trực tiếp (Kiến trúc / Kết cấu / MEP / BIM / PM)</p>
          </div>
          <span class="badge ok">${state.projects.length} Dự Án Trọng Điểm</span>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Mã DA</th>
                <th>Tên Dự Án & Chủ Đầu Tư</th>
                <th>Giá Trị Hợp Đồng</th>
                <th>CP Nhân Công Kỹ Thuật</th>
                <th>Lợi Nhuận Gộp DA (Margin)</th>
                <th>Tỷ Suất LN Gộp (%)</th>
                <th>Số Giờ Log (Timesheet)</th>
                <th>Đội Ngũ Kỹ Thuật</th>
                <th>Quản Lý Dự Án (PM)</th>
                <th>Thao Tác</th>
              </tr>
            </thead>
            <tbody>
              ${state.projects.map(p=>{
                const directLaborCost = p.spentHours * p.rate;
                const grossProfit = p.contractValue - directLaborCost;
                const marginPct = (grossProfit / p.contractValue * 100).toFixed(1);
                return `
                <tr style="cursor:pointer" onclick="openDrillModal('project_${p.code}')">
                  <td><b>${esc(p.code)}</b></td>
                  <td><b>${esc(p.name)}</b></td>
                  <td>${fmtMoney(p.contractValue)} đ</td>
                  <td><b style="color:#1c5b91">${fmtMoney(directLaborCost)} đ</b></td>
                  <td><b style="color:#17795e">${fmtMoney(grossProfit)} đ</b></td>
                  <td><span class="badge ok">${marginPct}%</span></td>
                  <td><b>${p.spentHours}h</b> / ${p.budgetHours}h</td>
                  <td><span class="source-chip">${p.teamCount} KTS/Kỹ sư</span></td>
                  <td><b>${esc(p.pm)}</b></td>
                  <td><button class="secondary" style="padding:4px 8px;font-size:9px">🔍 Xem Nhân Sự</button></td>
                </tr>`;
              }).join('')}
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- MODAL TRUY VẤN DRILL-DOWN -->
    <div id="drillModal" class="drill-modal">
      <div class="drill-box">
        <div class="drill-header">
          <div>
            <h2 id="drillTitle">Chi Tiết Phân Tích Dữ Liệu</h2>
            <p id="drillSub">Truy vấn nhân sự và số liệu liên quan theo thời gian thực</p>
          </div>
          <button class="drill-close" onclick="closeDrillModal()">✕</button>
        </div>
        <div class="drill-body" id="drillContent"></div>
      </div>
    </div>
    `;
  }

  // Giao diện tiêu chuẩn nhân viên
  const totalTsHours = (state.localTimesheets.reduce((a,x)=>a+(x.minutes||0),0)/60)+40;
  return `<div class="grid">
    ${card('Xin chào, '+esc(m.fullName),'Cổng thông tin nhân sự & dự án CPG Vietnam',`
      <div class="notice">
        <b>Mã nhân viên: ${esc(m.staffCode)}</b> | Chức danh: ${esc(m.jobTitle)} | Văn phòng: ${esc(m.office)} | Email: ${esc(m.email)}<br>
        Tài khoản đã được xác thực an toàn qua <b>Microsoft Entra ID (SSO @cpg.com.vn)</b>. Hệ thống liên kết trực tiếp với <b>BRAVO Payroll</b>.
      </div>
    `,'span12')}
    ${card('Hệ Thống Timesheet & Phân Tách Nghiệp Vụ','Quy định chuẩn CPG Vietnam',`
      <div class="notice ok">
        📌 <b>Quy định ghi nhận công:</b><br>
        • <b>Khối Kỹ thuật (Kiến trúc / Kết cấu / MEP / BIM / PM):</b> Bắt buộc log giờ làm việc theo mã dự án để nghiệm thu chi phí công trình.<br>
        • <b>Khối Vận hành (HR, BD, Kế toán, Hợp đồng QS, Lãnh đạo):</b> Không log timesheet dự án; được tự động ghi nhận công chuẩn theo chế độ điều hành doanh nghiệp.
      </div>
    `,'span12')}
    ${card('Lối tắt thao tác nhanh (Quick Actions)','Dành cho Kỹ sư & Kiến trúc sư',`
      <div class="quick-grid">
        <button class="quick" data-goto="timesheet"><b>⏱️ Log Timesheet 1 chạm</b><span>Sao chép nhanh giờ dự án tuần</span></button>
        <button class="quick" data-goto="attendance"><b>📍 Check-in Hiện trường GPS</b><span>Điểm danh tại công trường/khách hàng</span></button>
        <button class="quick" data-goto="leave"><b>📅 Đăng ký Nghỉ phép</b><span>Tự động tính ngày phép còn lại</span></button>
        <button class="quick" data-goto="pay"><b>💵 Xem Phiếu lương e-Payslip</b><span>Tra cứu chi tiết lương & thuế</span></button>
      </div>
    `,'span12')}
  </div>`;
};

// ==========================================
// DRILL-DOWN LOGIC VÀ GIẢI TRÌNH NHÂN SỰ
// ==========================================
window.openDrillModal=function(key){
  const modal=$('drillModal');
  const title=$('drillTitle');
  const sub=$('drillSub');
  const content=$('drillContent');
  if(!modal) return;

  const staffList = state.contacts.length ? state.contacts : [
    {staffCode:'FN25',fullName:'Soh Wee Keong',office:'HCM',jobTitle:'Chief Executive Officer (CEO) of CPG International',email:'soh.wee.keong@cpgcorp.com.sg'},
    {staffCode:'FN27',fullName:'Chung Keat Wei',office:'HN',jobTitle:'Deputy General Director (CPG Vietnam) cum General Manager Hanoi',email:'chung.keat.wei@cpgcorp.com.sg'},
    {staffCode:'H0072',fullName:'Nguyễn Văn A',office:'HCM',jobTitle:'Kiến trúc sư Trưởng (Lead Architect)',email:'h0072@cpg.com.vn'},
    {staffCode:'H0107',fullName:'Trần Thị B',office:'HCM',jobTitle:'Trưởng Dự án PM (Senior Project Manager)',email:'h0107@cpg.com.vn'},
    {staffCode:'S0007',fullName:'Lê Hoàng C',office:'HN',jobTitle:'Chuyên viên Nhân sự (HR Specialist)',email:'s0007@cpg.com.vn'},
    {staffCode:'S0316',fullName:'Phạm Minh D',office:'HN',jobTitle:'Kế toán Tiền lương (Payroll Accountant)',email:'s0316@cpg.com.vn'},
    {staffCode:'S0082',fullName:'Vũ Quốc E',office:'HCM',jobTitle:'Kỹ sư Kết cấu Chính (Senior Structural Engineer)',email:'s0082@cpg.com.vn'},
    {staffCode:'H0015',fullName:'Đỗ Hải Trang',office:'HCM',jobTitle:'Senior Manager - Finance & HR Admin',email:'do.hai.trang@cpgcorp.com.sg'}
  ];

  if(key==='drill_bd' || key==='drill_bd_pipeline'){
    title.textContent = 'Báo Cáo Hiệu Suất Khối Phát Triển Kinh Doanh (BD Team)';
    sub.textContent = 'Chỉ tiêu Doanh số Hợp đồng Ký mới Quý 3/2026: $2,000,000 USD (Target)';
    content.innerHTML = `
      <div class="drill-ai-insight">
        <b>💡 Đánh giá Kết Quả Kinh Doanh từ Ban Điều Hành:</b><br>
        Khối BD đã hoàn thành xuất sắc chỉ tiêu Quý 3/2026 với tổng giá trị hợp đồng tư vấn thiết kế ký mới đạt <b>$2,150,000 USD</b> (~ 53.7 Tỷ VNĐ), vượt <b>107.5%</b> kế hoạch. Các hợp đồng tiêu biểu: Giai đoạn 2 <i>Masteri Thảo Điền</i> và gói tư vấn thiết kế <i>Landmark Tower</i>.
      </div>
      <h3 style="margin-bottom:8px">Danh mục Hợp đồng Ký mới trong Quý (Business Development Pipeline):</h3>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Tên Dự Án Ký Mới</th><th>Chủ Đầu Tư</th><th>Giá Trị Hợp Đồng (USD)</th><th>Giá Trị (VNĐ)</th><th>Phụ Trách BD</th><th>Tình Trạng</th></tr></thead>
          <tbody>
            <tr><td><b>Khu Phức Hợp Masteri (Giai đoạn 2)</b></td><td>Tập đoàn BDS Masterise</td><td>$950,000 USD</td><td>23.750.000.000 đ</td><td>BD Team HCM</td><td><span class="badge ok">Đã ký HĐ chính thức</span></td></tr>
            <tr><td><b>Khách Sạn 5 Sao Landmark HN</b></td><td>Tập đoàn KS Quốc Tế</td><td>$750,000 USD</td><td>18.750.000.000 đ</td><td>BD Team HN</td><td><span class="badge ok">Đã ký HĐ chính thức</span></td></tr>
            <tr><td><b>TT Triển Lãm & Hội Nghị Quốc Tế</b></td><td>Liên danh Nhà nước - CĐT</td><td>$450,000 USD</td><td>11.250.000.000 đ</td><td>BD Team HN</td><td><span class="badge ok">Trúng thầu thiết kế</span></td></tr>
            <tr style="background:#eaf4ee;font-weight:800">
              <td colspan="2">TỔNG GIÁ TRỊ HỢP ĐỒNG KÝ MỚI QUÝ 3</td>
              <td style="color:#17795e">$2,150,000 USD</td>
              <td style="color:#17795e">53.750.000.000 đ</td>
              <td colspan="2"><span class="badge ok">VƯỢT 107.5% KPI</span></td>
            </tr>
          </tbody>
        </table>
      </div>
    `;
  } else if(key==='drill_support_team'){
    title.textContent = 'Khối Quản Trị / Gián Tiếp & Hỗ Trợ (24 Nhân Sự)';
    sub.textContent = 'HR, BD, Kế toán, Quản lý Hợp đồng QS & Ban Giám Đốc (Chi phí G&A Overhead)';
    content.innerHTML = `
      <div class="drill-ai-insight">
        <b>💡 Nguyên tắc hạch toán chi phí gián tiếp (G&A):</b><br>
        24 nhân sự thuộc khối Quản trị & Hỗ trợ không trực tiếp tham gia thiết kế từng công trình nên <b>không log timesheet dự án</b>. Chi phí lương của nhóm này (<b>895 Triệu VNĐ/tháng</b>) được hạch toán vào Chi phí Quản lý Doanh nghiệp (G&A) và kiểm soát ở mức <b>4.9%</b> doanh thu (mức an toàn cao của tập đoàn).
      </div>
      <h3 style="margin-bottom:8px">Danh sách Nhân sự Khối Điều hành & Hỗ trợ:</h3>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Mã NV</th><th>Họ và Tên</th><th>Bộ Phận</th><th>Chức Danh</th><th>Văn Phòng</th><th>KPI Trọng Tâm</th></tr></thead>
          <tbody>
            <tr><td><b>FN25</b></td><td><b>Soh Wee Keong</b></td><td>Ban Giám Đốc</td><td>Chief Executive Officer (CEO) of CPG International</td><td>HCM & HN</td><td>Lợi nhuận ròng & Tăng trưởng</td></tr>
            <tr><td><b>FN27</b></td><td><b>Chung Keat Wei</b></td><td>Ban Giám Đốc</td><td>Deputy General Director (CPG VN) / GM Hanoi</td><td>HN</td><td>Vận hành khối Hà Nội & BD</td></tr>
            <tr><td><b>H0015</b></td><td><b>Đỗ Hải Trang</b></td><td>Tài Chính & HR</td><td>Senior Manager - Finance & HR Admin</td><td>HCM</td><td>Quản trị Quỹ lương & Đối soát BRAVO</td></tr>
            <tr><td><b>S0007</b></td><td><b>Lê Hoàng C</b></td><td>Nhân Sự</td><td>Chuyên viên Nhân sự</td><td>HN</td><td>Tuyển dụng & Giữ chân nhân tài</td></tr>
            <tr><td><b>S0316</b></td><td><b>Phạm Minh D</b></td><td>Kế Toán</td><td>Kế toán Tiền lương</td><td>HN</td><td>Khớp lương BRAVO 100% Zero-Variance</td></tr>
          </tbody>
        </table>
      </div>
    `;
  } else if(key.startsWith('project_')){
    const code = key.replace('project_','');
    const prj = state.projects.find(p=>p.code===code) || state.projects[0];
    const directLabor = prj.spentHours * prj.rate;
    const grossMargin = prj.contractValue - directLabor;
    title.textContent = `Chi Tiết Dự Án: ${prj.code} — ${prj.name}`;
    sub.textContent = `PM: ${prj.pm} | Giá trị HĐ: ${fmtMoney(prj.contractValue)} VNĐ | Lợi nhuận gộp: ${fmtMoney(grossMargin)} VNĐ (${(grossMargin/prj.contractValue*100).toFixed(1)}%)`;
    content.innerHTML = `
      <div class="drill-ai-insight">
        <b>💡 Báo Cáo Hiệu Quả Dự Án:</b><br>
        Dự án do <b>${prj.teamCount} Kỹ sư & Kiến trúc sư</b> trực tiếp thực hiện. Tổng giờ log Timesheet là <b>${prj.spentHours}h</b>. Chi phí nhân công trực tiếp: <b style="color:#1c5b91">${fmtMoney(directLabor)} VNĐ</b>. Tỷ suất Lợi Nhuận Gộp dự án đạt <b style="color:#17795e">${(grossMargin/prj.contractValue*100).toFixed(1)}%</b>.
      </div>
      <h3 style="margin-bottom:8px">Kỹ sư & Kiến trúc sư trực tiếp log công (${prj.teamCount} nhân sự):</h3>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Mã NV</th><th>Họ tên</th><th>Bộ phận Kỹ thuật</th><th>Giờ log DA</th><th>Đơn giá/h</th><th>Chi phí hạch toán</th><th>Đánh giá</th></tr></thead>
          <tbody>
            ${staffList.slice(2,8).map((s,idx)=>`
              <tr>
                <td><b>${s.staffCode}</b></td>
                <td>${s.fullName}</td>
                <td>${s.jobTitle}</td>
                <td><b>${(prj.spentHours / 6 - idx*4).toFixed(0)}h</b></td>
                <td>${fmtMoney(prj.rate)} đ</td>
                <td><b style="color:#1c5b91">${fmtMoney((prj.spentHours / 6 - idx*4) * prj.rate)} đ</b></td>
                <td><span class="badge ok">Đúng tiến độ</span></td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;
  } else {
    title.textContent = 'Phân Tích Chi Phí & Lợi Nhuận Toàn Công Ty';
    sub.textContent = 'Tách bạch Doanh thu, Chi phí Kỹ thuật trực tiếp và Chi phí Quản lý G&A';
    content.innerHTML = `
      <div class="drill-ai-insight">
        <b>💡 Cơ Cấu Lợi Nhuận Doanh Nghiệp (CPG Vietnam):</b><br>
        Tổng Doanh Thu Hợp Đồng: <b>18.2 Tỷ VNĐ</b>.<br>
        - Chi phí Nhân công Kỹ thuật trực tiếp (116 NS): <b>3.93 Tỷ VNĐ</b> (21.6% DT).<br>
        - Chi phí Quản lý Gián tiếp G&A (24 NS: HR, BD, Kế toán, Lãnh đạo): <b>0.895 Tỷ VNĐ</b> (4.9% DT).<br>
        $\\rightarrow$ <b>Lợi Nhuận Thuần Hoạt Động (EBITDA):</b> <b style="color:#17795e">6.425 Tỷ VNĐ</b> (35.3% Doanh thu).
      </div>
    `;
  }

  modal.classList.add('open');
};

window.closeDrillModal=function(){
  const modal=$('drillModal');
  if(modal) modal.classList.remove('open');
};

// ==========================================
// CÁC TRANG PHỤ TRỢ KHÁC
// ==========================================
pages.exec_pnl=async()=>{
  return `<div class="grid">
    ${card('Báo Cáo Tài Chính Lợi Nhuận (P&L) & Quỹ Lương Tổng Thể','Phân tầng chuẩn giữa Chi phí Dự án Trực tiếp (Direct Labor) và Chi phí Quản lý Gián tiếp (G&A Overhead)',`
      <div class="metrics" style="margin-bottom:14px">
        <div class="metric"><b style="color:#12365c">18.200.000.000 đ</b><span>Tổng Doanh Thu Hợp Đồng</span></div>
        <div class="metric"><b style="color:#1c5b91">3.930.000.000 đ</b><span>Chi phí Kỹ thuật (116 NS)</span></div>
        <div class="metric"><b style="color:#9f6d0e">895.000.000 đ</b><span>Chi phí G&A / BD (24 NS)</span></div>
        <div class="metric"><b style="color:#17795e">6.425.000.000 đ</b><span>Lợi Nhuận Thuần (35.3%)</span></div>
      </div>

      <div class="table-wrap" style="margin-bottom:14px">
        <table>
          <thead>
            <tr>
              <th>Khoản Mục Tài Chính</th>
              <th>Số Nhân Sự</th>
              <th>Số Tiền (VNĐ)</th>
              <th>Tỷ Trọng / Doanh Thu</th>
              <th>Phân Loại Hạch Toán Kế Toán</th>
              <th>Trạng Thái Đối Soát BRAVO</th>
            </tr>
          </thead>
          <tbody>
            <tr style="background:#f4f8fb;font-weight:700">
              <td>I. TỔNG DOANH THU TƯ VẤN THIẾT KẾ</td>
              <td>-</td>
              <td>18.200.000.000 đ</td>
              <td>100.0%</td>
              <td>Doanh thu ghi nhận theo tiến độ</td>
              <td><span class="badge ok">Đã xuất hóa đơn</span></td>
            </tr>
            <tr>
              <td><b>1. Chi phí Lương Khối Kỹ Thuật (Kiến trúc / BIM / MEP / PM)</b></td>
              <td>116 người</td>
              <td>3.930.000.000 đ</td>
              <td>21.6%</td>
              <td>Chi phí Nhân công Trực tiếp (Direct Labor)</td>
              <td><span class="badge ok">Khớp 100% (BRAVO)</span></td>
            </tr>
            <tr style="background:#eaf4ee;font-weight:800">
              <td>II. LỢI NHUẬN GỘP DỰ ÁN (PROJECT GROSS MARGIN)</td>
              <td>116 người</td>
              <td style="color:#17795e">14.270.000.000 đ</td>
              <td style="color:#17795e">78.4%</td>
              <td>Lợi nhuận gộp sau chi phí kỹ thuật</td>
              <td><span class="badge ok">Rất Tốt</span></td>
            </tr>
            <tr>
              <td><b>2. Chi phí Khối Quản trị & Hỗ trợ (HR, Kế toán, QS)</b></td>
              <td>18 người</td>
              <td>545.000.000 đ</td>
              <td>3.0%</td>
              <td>Chi phí Quản lý Doanh nghiệp (G&A Overhead)</td>
              <td><span class="badge ok">Khớp 100% (BRAVO)</span></td>
            </tr>
            <tr>
              <td><b>3. Chi phí Khối Phát triển Kinh doanh (BD) & Ban Lãnh đạo</b></td>
              <td>6 người</td>
              <td>350.000.000 đ</td>
              <td>1.9%</td>
              <td>Chi phí Bán hàng & Quản lý Cấp cao</td>
              <td><span class="badge ok">Khớp 100% (BRAVO)</span></td>
            </tr>
            <tr style="background:#edf4fa;font-weight:800">
              <td>III. TỔNG QUỸ LƯƠNG TOÀN CÔNG TY THÁNG 8 (1 + 2 + 3)</td>
              <td>140 người</td>
              <td style="color:#12365c">4.825.000.000 đ</td>
              <td>26.5%</td>
              <td>Chi phí Tiền lương Toàn Cty</td>
              <td><span class="badge ok">Zero Variance (BRAVO)</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="actions">
        <button class="primary" onclick="toast('Đã xuất Báo cáo P&L Doanh nghiệp tháng 08/2026 (PDF/Excel)')">📥 Xuất Báo Cáo P&L Doanh Nghiệp (PDF)</button>
      </div>
    `,'span12')}
  </div>`;
};

pages.exec_utilization=async()=>{
  return `<div class="grid">
    ${card('Hiệu Suất Khối Kỹ Thuật (116 Nhân Sự Kỹ Thuật Log Timesheet)','Phân tích giờ Billable phục vụ dự án theo từng bộ phận chuyên môn',`
      <div class="metrics" style="margin-bottom:14px">
        <div class="metric"><b style="color:#17795e">91.0%</b><span>Tỷ lệ Billable Khối Kỹ thuật</span></div>
        <div class="metric"><b style="color:#12365c">18.560h</b><span>Tổng giờ làm việc kỹ thuật</span></div>
        <div class="metric"><b style="color:#17795e">16.890h</b><span>Giờ hạch toán dự án (Billable)</span></div>
        <div class="metric"><b style="color:#9f6d0e">1.670h</b><span>Giờ đào tạo quy chuẩn & họp (9.0%)</span></div>
      </div>

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Bộ Phận Kỹ Thuật</th>
              <th>Quân Số</th>
              <th>Giờ Billable DA</th>
              <th>Giờ Nội Bộ</th>
              <th>Tỷ Lệ Khai Thác</th>
              <th>Năng Suất TB</th>
              <th>Đánh Giá</th>
            </tr>
          </thead>
          <tbody>
            <tr><td><b>Phòng Kiến trúc 1 (HCM)</b></td><td>28 KTS</td><td>4.520h</td><td>400h</td><td><b style="color:#17795e">91.9%</b></td><td>175.7 h/người</td><td><span class="badge ok">Xuất sắc</span></td></tr>
            <tr><td><b>Phòng Kiến trúc 2 (HN)</b></td><td>20 KTS</td><td>3.150h</td><td>370h</td><td><b style="color:#17795e">89.5%</b></td><td>176.0 h/người</td><td><span class="badge ok">Rất tốt</span></td></tr>
            <tr><td><b>Phòng Kết cấu Công trình (CS)</b></td><td>30 Kỹ sư</td><td>4.780h</td><td>500h</td><td><b style="color:#17795e">90.5%</b></td><td>176.0 h/người</td><td><span class="badge ok">Xuất sắc</span></td></tr>
            <tr><td><b>Phòng Cơ điện (MEP)</b></td><td>22 Kỹ sư</td><td>3.410h</td><td>250h</td><td><b style="color:#17795e">93.2%</b></td><td>166.4 h/người</td><td><span class="badge ok">Xuất sắc</span></td></tr>
            <tr><td><b>Phòng Quản lý Dự án & BIM</b></td><td>16 PM/BIM</td><td>2.630h</td><td>150h</td><td><b style="color:#17795e">94.6%</b></td><td>173.8 h/người</td><td><span class="badge ok">Vượt chỉ tiêu</span></td></tr>
          </tbody>
        </table>
      </div>
    `,'span12')}
  </div>`;
};

pages.people=async()=>{
  const rows = state.contacts.length ? state.contacts : [
    {staffCode:'FN25',fullName:'Soh Wee Keong',office:'HCM',jobTitle:'Chief Executive Officer (CEO) of CPG International',workingDays:'Monday - Friday',email:'soh.wee.keong@cpgcorp.com.sg'},
    {staffCode:'FN27',fullName:'Chung Keat Wei',office:'HN',jobTitle:'Deputy General Director (CPG Vietnam) cum GM Hanoi',workingDays:'Monday - Friday',email:'chung.keat.wei@cpgcorp.com.sg'},
    {staffCode:'H0015',fullName:'Đỗ Hải Trang',office:'HCM',jobTitle:'Senior Manager - Finance & HR Admin',workingDays:'Monday - Friday',email:'do.hai.trang@cpgcorp.com.sg'},
    {staffCode:'H0072',fullName:'Nguyễn Văn A',office:'HCM',jobTitle:'Kiến trúc sư Trưởng (Lead Architect)',workingDays:'Monday - Friday',email:'h0072@cpg.com.vn'},
    {staffCode:'H0107',fullName:'Trần Thị B',office:'HCM',jobTitle:'Trưởng Dự án PM (Senior Project Manager)',workingDays:'Monday - Friday',email:'h0107@cpg.com.vn'},
    {staffCode:'S0007',fullName:'Lê Hoàng C',office:'HN',jobTitle:'Chuyên viên Nhân sự (HR Specialist)',workingDays:'Monday - Friday',email:'s0007@cpg.com.vn'},
    {staffCode:'S0316',fullName:'Phạm Minh D',office:'HN',jobTitle:'Kế toán Tiền lương (Payroll Accountant)',workingDays:'Monday - Friday',email:'s0316@cpg.com.vn'},
    {staffCode:'S0082',fullName:'Vũ Quốc E',office:'HCM',jobTitle:'Kỹ sư Kết cấu Chính (Senior Structural Engineer)',workingDays:'Monday - Friday',email:'s0082@cpg.com.vn'}
  ];
  return `<div class="grid">${card('Danh bạ 140 Nhân sự CPG Việt Nam','Dữ liệu chính thức từ CPG Contact List - Aug 2026 (101 HCM + 39 HN)',`
    <div class="toolbar-row">
      <input class="search" id="peopleSearch" placeholder="Tìm kiếm theo mã nhân viên, họ tên, chức danh, văn phòng, email...">
      <span class="badge ok">${state.contacts.length||140} nhân sự</span>
    </div>
    <div id="peopleTable">${peopleTable(rows)}</div>
  `,'span12')}</div>`;
};

function peopleTable(rows){
  return `<div class="table-wrap"><table>
    <thead><tr><th>Mã NV</th><th>Họ và Tên</th><th>Văn phòng</th><th>Chức danh / Vị trí</th><th>Lịch làm việc</th><th>Email Doanh nghiệp</th></tr></thead>
    <tbody>${rows.map(x=>`<tr><td><b>${esc(x.staffCode)}</b></td><td>${esc(x.fullName)}</td><td><span class="badge ${x.office.includes('HCM')?'ok':'warn'}">${esc(x.office)}</span></td><td>${esc(x.jobTitle)}</td><td>${esc(x.workingDays)}</td><td>${esc(x.email)}</td></tr>`).join('')}</tbody>
  </table></div>`;
}

pages.timesheet=async()=>{
  const now=new Date(),monday=new Date(now);
  monday.setDate(now.getDate()-((now.getDay()+6)%7));
  const from=monday.toISOString().slice(0,10);
  const opts=state.projects.map(p=>`<option value="${esc(p.code)}">${esc(p.code)} - ${esc(p.name)}</option>`).join('');
  
  const entries = [
    {date:from, project:'DEMO-001', desc:'Thiết kế mặt bằng kiến trúc tầng 5-8', hours:8, status:'APPROVED'},
    {date:new Date(monday.getTime()+86400000).toISOString().slice(0,10), project:'DEMO-001', desc:'Phối hợp mô hình MEP & Kết cấu', hours:8, status:'APPROVED'},
    {date:new Date(monday.getTime()+2*86400000).toISOString().slice(0,10), project:'PRJ-002', desc:'Khảo sát hiện trường & kiểm tra cốt sàn', hours:8, status:'APPROVED'},
    {date:new Date(monday.getTime()+3*86400000).toISOString().slice(0,10), project:'PRJ-002', desc:'Chỉnh sửa bản vẽ chi tiết theo ý kiến CĐT', hours:8, status:'APPROVED'},
    {date:new Date(monday.getTime()+4*86400000).toISOString().slice(0,10), project:'DEMO-001', desc:'Tổng hợp hồ sơ bàn giao giai đoạn 1', hours:8, status:'APPROVED'},
    ...state.localTimesheets.map(t=>({date:t.workDate, project:t.projectCode, desc:t.description, hours:t.minutes/60, status:'PENDING'}))
  ];
  
  const totalHours = entries.reduce((a,r)=>a+Number(r.hours),0);

  return `<div class="grid">
    ${card('Ghi nhận Giờ làm việc Dự án (Timesheet Kỹ Thuật)','Dành riêng cho Khối Kỹ thuật (116 NS: Kiến trúc, Kết cấu, MEP, BIM, PM). Khối Quản lý/BD tự động ghi nhận công chuẩn.',`
      <div class="actions" style="margin-bottom:12px;background:#e9f2f9;padding:10px;border-radius:12px">
        <button class="primary" id="btnQuickCopy" type="button">⚡ Sao chép tuần trước (1 chạm - 40h)</button>
        <button class="secondary" id="btnQuick5d" type="button">📝 Điền nhanh 8h/ngày cả tuần</button>
        <span style="font-size:10px;color:#1c5b91;font-weight:600;margin-left:auto">Tự động khóa công vào 18h00 thứ Sáu hàng tuần</span>
      </div>
      <form id="tsForm" class="form-grid">
        <div class="field"><label>Ngày làm việc</label><input name="date" type="date" value="${from}"></div>
        <div class="field wide"><label>Mã Dự Án CPG</label><select name="project">${opts}</select></div>
        <div class="field"><label>Số giờ làm</label><input name="hours" type="number" value="8" step="0.25"></div>
        <div class="field full"><label>Mô tả chi tiết công việc kỹ thuật</label><input name="desc" placeholder="VD: Triển khai bản vẽ kiến trúc chi tiết, tính toán tải trọng kết cấu..."></div>
        <div class="field full">
          <div class="actions">
            <button class="primary" type="submit">Lưu Timesheet</button>
          </div>
        </div>
      </form>
    `,'span12',`<span class="badge ${totalHours>=40?'ok':'warn'}">Tổng tuần: ${totalHours.toFixed(1)} / 40h</span>`)}
    ${card('Bảng công tuần này','Chi tiết các ngày trong tuần (Thứ 2 - Thứ 6)',`
      <div class="table-wrap">
        <table>
          <thead><tr><th>Ngày</th><th>Mã Dự Án</th><th>Nội dung công việc</th><th>Số giờ</th><th>Trạng thái</th></tr></thead>
          <tbody>
            ${entries.map(r=>`<tr><td>${fmtDate(r.date)}</td><td><b>${esc(r.project)}</b></td><td>${esc(r.desc)}</td><td><b>${r.hours}h</b></td><td>${badge(r.status)}</td></tr>`).join('')}
          </tbody>
        </table>
      </div>
    `,'span12')}
  </div>`;
};

pages.attendance=async()=>{
  const today=new Date().toISOString().slice(0,10);
  const otHoursUsed = 12;
  const otLimit = 40;

  return `<div class="grid">
    ${card('📍 Chấm công Hiện trường / Công tác (Mobile GPS Field Check-in)','Dành cho Kỹ sư giám sát & Kiến trúc sư ra công trình, gặp khách hàng',`
      <div class="gps-box">
        <div class="gps-loc"><span>📍 Vị trí GPS hiện tại:</span> <b>10.7769° N, 106.7009° E (Dự án Masteri Thảo Điền)</b></div>
        <form id="fieldCheckinForm" class="form-grid">
          <div class="field wide"><label>Dự án / Địa điểm Công tác</label><select name="project">${state.projects.map(p=>`<option value="${p.code}">${p.code} - ${p.name}</option>`).join('')}</select></div>
          <div class="field wide"><label>Mục đích công tác</label><input name="purpose" value="Giám sát thi công & Họp ban QLDA"></div>
          <div class="field full"><button class="primary" type="submit">📸 Xác nhận Check-in Hiện trường</button></div>
        </form>
      </div>
      <h3 style="margin:14px 0 8px">Lịch sử Check-in Hiện trường gần đây:</h3>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Ngày</th><th>Giờ</th><th>Dự án</th><th>Vị trí GPS</th><th>Nội dung</th><th>Trạng thái</th></tr></thead>
          <tbody>
            ${state.fieldCheckins.map(c=>`<tr><td>${c.date}</td><td>${c.time}</td><td><b>${c.project}</b></td><td>${c.location}</td><td>${c.type}</td><td>${badge(c.status)}</td></tr>`).join('')}
          </tbody>
        </table>
      </div>
    `,'span12')}

    ${card('Đăng ký Làm thêm giờ (OT) & Cảnh báo Trần Luật Lao Động','Quy định: Tối đa 40 giờ/tháng theo Bộ luật Lao động',`
      <div class="${otHoursUsed>=30 ? (otHoursUsed>=40?'ot-guard-bad':'ot-guard-warn') : 'notice'}">
        <span>⏱️ <b>Đã tích lũy OT trong tháng:</b> ${otHoursUsed}h / ${otLimit}h trần luật định. ${otHoursUsed>=30?'(Gần chạm trần 40h/tháng)':''}</span>
      </div>
      <form id="otForm" class="form-grid" style="margin-top:10px">
        <div class="field"><label>Ngày làm thêm</label><input name="date" type="date" value="${today}"></div>
        <div class="field"><label>Số phút làm thêm</label><input name="minutes" type="number" value="120" step="30"></div>
        <div class="field"><label>Dự án phát sinh OT</label><select name="project">${state.projects.map(p=>`<option value="${p.code}">${p.code}</option>`).join('')}</select></div>
        <div class="field"><label>Lý do cấp bách</label><input name="reason" value="Tập trung nộp hồ sơ thiết kế kịp tiến độ CĐT"></div>
        <div class="field full"><button class="primary" type="submit">Gửi duyệt Đơn OT</button></div>
      </form>
    `,'span12')}
  </div>`;
};

pages.leave=async()=>{
  const today=new Date().toISOString().slice(0,10);
  return `<div class="grid">
    ${card('Đăng ký Nghỉ phép Mới','Tự động trừ vào quỹ phép năm và gửi Quản lý phê duyệt',`
      <form id="leaveForm" class="form-grid">
        <div class="field"><label>Loại nghỉ phép</label><select name="type"><option value="ANNUAL">Nghỉ phép năm (Có hưởng lương)</option><option value="SICK">Nghỉ ốm / Khám bệnh (Hưởng BHXH)</option><option value="UNPAID">Nghỉ việc riêng không hưởng lương</option><option value="MATERNITY">Nghỉ chế độ thai sản</option></select></div>
        <div class="field"><label>Từ ngày</label><input name="from" type="date" value="${today}"></div>
        <div class="field"><label>Đến ngày</label><input name="to" type="date" value="${today}"></div>
        <div class="field"><label>Số giờ / ngày</label><input name="hours" type="number" value="8" step="4"></div>
        <div class="field wide"><label>Lý do nghỉ phép</label><input name="reason" placeholder="VD: Giải quyết việc gia đình cá nhân..."></div>
        <div class="field wide"><label>Nhân sự bàn giao công việc</label><input name="handover" placeholder="VD: Bàn giao dự án CPG Tower"></div>
        <div class="field full"><button class="primary" type="submit">Gửi đơn xin nghỉ phép</button></div>
      </form>
    `,'span12')}
  </div>`;
};

pages.performance=async()=>{
  return `<div class="grid">
    ${card('Đánh giá Hiệu suất Chuyên Biệt Theo Từng Khối (KPI Evaluation)','Kỳ đánh giá: Quý 3 / 2026',`
      <div class="split">
        <div class="cost-card">
          <b>1. Khối Kỹ Thuật (Kiến trúc / Kết cấu / MEP / BIM):</b><br>
          <span style="font-size:9px;color:var(--muted)">• Tiến độ bàn giao đúng hạn (Trọng số 50%)<br>• Chất lượng kỹ thuật & ít lỗi bản vẽ (30%)<br>• Tuân thủ timesheet & an toàn (20%)</span>
        </div>
        <div class="cost-card" style="border-left-color:#f59e0b">
          <b>2. Khối Phát Triển Kinh Doanh (BD Team):</b><br>
          <span style="font-size:9px;color:var(--muted)">• <b>Chỉ tiêu Doanh số ký mới:</b> <b style="color:#17795e">$2,000,000 USD / Quý</b><br>• Tỷ lệ thắng thầu (Win Rate $\\ge 40\\%$)<br>• Mở rộng quan hệ CĐT lớn</span>
        </div>
      </div>
    `,'span12')}
  </div>`;
};

pages.costing=async()=>{
  return pages.exec_pnl();
};

pages.pay=async()=>{
  return `<div class="grid">
    ${card('Phiếu lương Điện tử (e-Payslip) - Tháng 08/2026','Nguồn dữ liệu tính toán chính thức: BRAVO Payroll Engine',`
      <div class="notice" style="margin-bottom:12px">
        <b>Mã nhân viên: H0072</b> | Họ tên: Nguyễn Văn A | Chức vụ: Kiến trúc sư Trưởng | Số ngày công chuẩn: 22 ngày
      </div>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Khoản mục Lương & Thu nhập</th><th>Số tiền (VNĐ)</th><th>Ghi chú</th></tr></thead>
          <tbody>
            <tr><td><b>1. Lương cơ bản theo hợp đồng</b></td><td><b>28.000.000 đ</b></td><td>Lương đóng BHXH</td></tr>
            <tr><td><b>2. Lương hiệu quả / Năng suất dự án</b></td><td><b>12.500.000 đ</b></td><td>Theo nghiệm thu Timesheet dự án</td></tr>
            <tr><td><b>3. Phụ cấp trách nhiệm & điện thoại</b></td><td><b>2.000.000 đ</b></td><td>Miễn thuế TNCN theo quy chế</td></tr>
            <tr><td><b>4. Tiền làm thêm giờ (OT 150% - 12h)</b></td><td><b>3.818.182 đ</b></td><td>Phần chênh lệch được miễn thuế</td></tr>
            <tr style="background:#f4f8fb"><td><b>TỔNG THU NHẬP (GROSS)</b></td><td><b style="color:#17795e;font-size:12px">46.318.182 đ</b></td><td></td></tr>
            <tr><td>5. Trừ BHXH, BHYT, BHTN (10.5%)</td><td>- 2.940.000 đ</td><td>Trích nộp cơ quan BHXH</td></tr>
            <tr><td>6. Giảm trừ gia cảnh (Bản thân + 1 người phụ thuộc)</td><td>- 15.400.000 đ</td><td>11tr bản thân + 4.4tr phụ thuộc</td></tr>
            <tr><td>7. Thuế Thu nhập cá nhân (TNCN) tạm khấu trừ</td><td>- 3.251.818 đ</td><td>Biểu thuế lũy tiến từng phần</td></tr>
            <tr style="background:#eaf4ee"><td><b>THỰC LĨNH CHUYỂN KHOẢN (NET)</b></td><td><b style="color:#126249;font-size:15px">40.126.364 đ</b></td><td><b>Chuyển khoản ngày 05 hàng tháng</b></td></tr>
          </tbody>
        </table>
      </div>
    `,'span12')}
  </div>`;
};

pages.profile=async()=>{
  const m=pmeta();
  return `<div class="grid">
    ${card('Hồ sơ Nhân viên CPG','Thông tin đồng bộ từ HR Master & Microsoft Entra ID',`
      <div class="record-list">
        <div class="record"><b>Họ và tên:</b> ${esc(m.fullName)} (${esc(m.staffCode)})</div>
        <div class="record"><b>Chức danh:</b> ${esc(m.jobTitle)}</div>
        <div class="record"><b>Văn phòng làm việc:</b> ${esc(m.office)}</div>
        <div class="record"><b>Email Doanh nghiệp:</b> ${esc(m.email)}</div>
        <div class="record"><b>Mã số thuế & Số sổ BHXH:</b> Đã định danh chuẩn hóa 100%</div>
      </div>
    `,'span12')}
  </div>`;
};

pages.approvals=async()=>{
  if(state.persona==='TRIAL-CEO'){
    return `<div class="grid">
      ${card('Phê Duyệt Cấp Ban Điều Hành (CEO Approvals)','Các quyết định trọng yếu cần chữ ký của Tổng Giám Đốc Soh Wee Keong',`
        <h3 style="margin-bottom:8px">1. Phê duyệt Bảng lương Tổng thể Toàn Công ty (Tháng 08/2026):</h3>
        <div class="cost-card" style="margin-bottom:14px;border-left-color:#17795e">
          <div style="display:flex;justify-content:space-between;align-items:center">
            <div>
              <b>Tổng Quỹ Lương: 4.825.000.000 VNĐ</b> (140 nhân sự HCM & HN)<br>
              <span style="font-size:10px;color:var(--muted)">Đã đối soát khớp 100% với BRAVO Payroll. Không có phát sinh chênh lệch.</span>
            </div>
            <div>
              ${state.payrollSigned ? '<span class="badge ok">ĐÃ PHÊ DUYỆT</span>' : '<button class="primary" id="btnCeoSign2" style="background:#17795e">Ký Phê Duyệt Chi Tiền</button>'}
            </div>
          </div>
        </div>
      `,'span12')}
    </div>`;
  }

  return `<div class="grid">
    ${card('Trung tâm Phê duyệt dành cho Quản lý / PM','Duyệt đơn nghỉ phép, duyệt giờ làm thêm và xác nhận Timesheet',`
      <div class="table-wrap">
        <table>
          <thead><tr><th>Nhân viên</th><th>Loại yêu cầu</th><th>Thời gian</th><th>Nội dung</th><th>Thao tác</th></tr></thead>
          <tbody>
            <tr>
              <td><b>Nguyễn Văn A (H0072)</b></td>
              <td>Nghỉ phép năm</td>
              <td>2026-09-02 (8h)</td>
              <td>Nghỉ bù lễ</td>
              <td><button class="success" onclick="toast('Đã phê duyệt đơn')">Duyệt</button> <button class="danger" onclick="toast('Từ chối')">Từ chối</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    `,'span12')}
  </div>`;
};

pages.team=async()=>{
  return pages.exec_utilization();
};

pages.hr=async()=>{
  return `<div class="grid">
    ${card('Vận hành Nhân sự (HR Operations)','Quản trị 140 nhân sự CPG, quản lý hợp đồng và tổng hợp công',`
      <div class="metrics" style="margin-bottom:14px">
        <div class="metric"><b>101</b><span>Nhân sự TP.HCM</span></div>
        <div class="metric"><b>39</b><span>Nhân sự Hà Nội</span></div>
        <div class="metric"><b>100%</b><span>Đã ký HĐLĐ chuẩn</span></div>
        <div class="metric"><b>0</b><span>Hồ sơ quá hạn</span></div>
      </div>
      <div class="actions">
        <button class="primary" onclick="toast('Đã xuất báo cáo tổng hợp công tháng')">📊 Xuất Bảng Tổng Hợp Công (Excel)</button>
        <button class="secondary" onclick="toast('Đã đồng bộ danh bạ với Microsoft Entra ID')">🔄 Đồng bộ Microsoft Entra ID</button>
      </div>
    `,'span12')}
  </div>`;
};

pages.payroll=async()=>{
  return `<div class="grid">
    ${card('Vận hành Tính lương & Đối soát BRAVO','Kiến trúc Hybrid an toàn: BRAVO là nguồn tính lương chính thức',`
      <div class="metrics" style="margin-bottom:14px">
        <div class="metric"><b style="color:#17795e">BRAVO</b><span>Official Payroll Engine</span></div>
        <div class="metric"><b>140 / 140</b><span>Nhân sự đã đối soát</span></div>
        <div class="metric"><b style="color:#17795e">0 VNĐ</b><span>Sai lệch (Zero Variance)</span></div>
        <div class="metric"><b>SẴN SÀNG</b><span>Trạng thái phát hành</span></div>
      </div>
      <div class="actions">
        <button class="primary" onclick="toast('Đã kích hoạt phát hành Phiếu lương e-Payslip tới 140 nhân sự')">🚀 Phát hành e-Payslip tới 140 nhân sự</button>
      </div>
    `,'span12')}
  </div>`;
};

pages.platform=async()=>{
  return `<div class="grid">
    ${card('Kiểm soát Hạ tầng & Cổng An Toàn (IT Admin)','Thông số môi trường và bảo mật',`
      <div class="metrics">
        <div class="metric"><b>.NET 10 Web API</b><span>Backend Engine</span></div>
        <div class="metric"><b>PostgreSQL 18</b><span>Database Core</span></div>
        <div class="metric"><b>BRAVO Adapter</b><span>SQL Server Connected</span></div>
        <div class="metric"><b>Microsoft Entra ID</b><span>SSO / MFA Enabled</span></div>
      </div>
    `,'span12')}
  </div>`;
};

function bindActions(){
  document.querySelectorAll('[data-goto]').forEach(b=>b.onclick=()=>openPage(b.dataset.goto));
  
  document.querySelectorAll('[data-drill]').forEach(el=>{
    el.style.cursor = 'pointer';
    el.onclick = () => openDrillModal(el.dataset.drill);
  });

  const ps=$('peopleSearch');
  if(ps)ps.oninput=()=>{
    const q=ps.value.trim().toLowerCase();
    const rows=(state.contacts.length?state.contacts:[
      {staffCode:'FN25',fullName:'Soh Wee Keong',office:'HCM',jobTitle:'Chief Executive Officer (CEO) of CPG International',workingDays:'Monday - Friday',email:'soh.wee.keong@cpgcorp.com.sg'},
      {staffCode:'FN27',fullName:'Chung Keat Wei',office:'HN',jobTitle:'Deputy General Director (CPG Vietnam) cum GM Hanoi',workingDays:'Monday - Friday',email:'chung.keat.wei@cpgcorp.com.sg'},
      {staffCode:'H0015',fullName:'Đỗ Hải Trang',office:'HCM',jobTitle:'Senior Manager - Finance & HR Admin',workingDays:'Monday - Friday',email:'do.hai.trang@cpgcorp.com.sg'},
      {staffCode:'H0072',fullName:'Nguyễn Văn A',office:'HCM',jobTitle:'Kiến trúc sư Trưởng (Lead Architect)',workingDays:'Monday - Friday',email:'h0072@cpg.com.vn'},
      {staffCode:'H0107',fullName:'Trần Thị B',office:'HCM',jobTitle:'Trưởng Dự án PM (Senior Project Manager)',workingDays:'Monday - Friday',email:'h0107@cpg.com.vn'},
      {staffCode:'S0007',fullName:'Lê Hoàng C',office:'HN',jobTitle:'Chuyên viên Nhân sự (HR Specialist)',workingDays:'Monday - Friday',email:'s0007@cpg.com.vn'},
      {staffCode:'S0316',fullName:'Phạm Minh D',office:'HN',jobTitle:'Kế toán Tiền lương (Payroll Accountant)',workingDays:'Monday - Friday',email:'s0316@cpg.com.vn'},
      {staffCode:'S0082',fullName:'Vũ Quốc E',office:'HCM',jobTitle:'Kỹ sư Kết cấu Chính (Senior Structural Engineer)',workingDays:'Monday - Friday',email:'s0082@cpg.com.vn'}
    ]).filter(x=>[x.staffCode,x.fullName,x.jobTitle,x.office,x.email].some(v=>String(v).toLowerCase().includes(q)));
    $('peopleTable').innerHTML=peopleTable(rows);
  };

  const ts=$('tsForm');
  if(ts)ts.onsubmit=e=>{
    e.preventDefault();
    const f=new FormData(ts);
    state.localTimesheets.push({
      workDate: f.get('date'),
      projectCode: f.get('project'),
      minutes: Math.round(Number(f.get('hours'))*60),
      description: f.get('desc')||'Ghi nhận giờ làm việc'
    });
    toast('Đã lưu Timesheet kỹ thuật thành công!');
    renderPage();
  };

  const btnQuickCopy=$('btnQuickCopy');
  if(btnQuickCopy)btnQuickCopy.onclick=()=>{
    toast('⚡ Đã sao chép lịch tuần trước (40h) thành công!');
    renderPage();
  };

  const btnQuick5d=$('btnQuick5d');
  if(btnQuick5d)btnQuick5d.onclick=()=>{
    toast('📝 Đã điền nhanh 8h/ngày cho tuần làm việc!');
    renderPage();
  };

  const fc=$('fieldCheckinForm');
  if(fc)fc.onsubmit=e=>{
    e.preventDefault();
    const f=new FormData(fc);
    const now=new Date();
    state.fieldCheckins.unshift({
      date: now.toISOString().slice(0,10),
      time: now.toTimeString().slice(0,5),
      project: f.get('project'),
      location: '10.7769° N, 106.7009° E (Hiện trường)',
      type: f.get('purpose'),
      status: 'APPROVED'
    });
    toast('📸 Điểm danh Hiện trường GPS thành công!');
    renderPage();
  };

  const ot=$('otForm');
  if(ot)ot.onsubmit=e=>{
    e.preventDefault();
    toast('Đã gửi đơn đăng ký làm thêm giờ (OT) tới PM!');
    renderPage();
  };

  const leave=$('leaveForm');
  if(leave)leave.onsubmit=e=>{
    e.preventDefault();
    toast('Đã gửi đơn xin nghỉ phép!');
    renderPage();
  };

  const btnCeoSign=$('btnCeoSign');
  if(btnCeoSign)btnCeoSign.onclick=()=>{
    state.payrollSigned = true;
    toast('⭐ TỔNG GIÁM ĐỐC SOH WEE KEONG ĐÃ KÝ DUYỆT CHI QUỸ LƯƠNG 4.825 TỶ THÀNH CÔNG!');
    renderPage();
  };

  const btnCeoSign2=$('btnCeoSign2');
  if(btnCeoSign2)btnCeoSign2.onclick=()=>{
    state.payrollSigned = true;
    toast('⭐ TỔNG GIÁM ĐỐC SOH WEE KEONG ĐÃ KÝ DUYỆT CHI QUỸ LƯƠNG 4.825 TỶ THÀNH CÔNG!');
    renderPage();
  };
}

async function boot(){
  try{
    const r=await fetch('./data/cpg_contacts_aug_2026.json');
    const j=await r.json();
    state.contacts=j.contacts||[];
  }catch{
    state.contacts=[];
  }
  $('key').value=state.key;
  $('persona').value=state.persona;
  $('persona').onchange=()=>{state.persona=$('persona').value;connect();};
  $('connect').onclick=connect;
  renderNav();
  setHeader();
  await connect();
}

boot();
