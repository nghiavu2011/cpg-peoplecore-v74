-- Synthetic LOCAL TRIAL fixtures only. Never production evidence and never real CPG employee/payroll data.
BEGIN;

INSERT INTO peoplecore.employee(id,staff_code,corporate_email,display_name,company_code,department_code,office_code,position_title,grade_code,manager_employee_id,timesheet_policy,employment_status,hire_date,effective_from,updated_at,row_version) VALUES
('11111111-1111-4111-8111-111111111111','TRIAL-MGR','trial-mgr@example.invalid','Phạm Quang Dũng','CPG-TRIAL','TRIAL-DESIGN','HN','Deputy General Manager (Hanoi Office) & IDD-BIM Manager',NULL,NULL,'REQUIRED','ACTIVE','2026-01-01','2026-01-01',now(),1),
('22222222-2222-4222-8222-222222222222','TRIAL-EMP','trial-emp@example.invalid','Vũ Trọng Nghĩa','CPG-TRIAL','TRIAL-DESIGN','HN','AI Visualization Specialist',NULL,'11111111-1111-4111-8111-111111111111','REQUIRED','ACTIVE','2026-01-01','2026-01-01',now(),1),
('33333333-3333-4333-8333-333333333333','TRIAL-HR','trial-hr@example.invalid','Phan Thị Mai Thảo','CPG-TRIAL','TRIAL-HR','HCM','HR Manager',NULL,NULL,'REQUIRED','ACTIVE','2026-01-01','2026-01-01',now(),1),
('44444444-4444-4444-8444-444444444444','TRIAL-PAY','trial-pay@example.invalid','Đỗ Hải Trang','CPG-TRIAL','TRIAL-PAYROLL','HCM','Senior Manager - Finance & HR Admin',NULL,NULL,'REQUIRED','ACTIVE','2026-01-01','2026-01-01',now(),1),
('55555555-5555-4555-8555-555555555555','TRIAL-ADMIN','trial-admin@example.invalid','Đặng Thanh Cường','CPG-TRIAL','TRIAL-IT','HCM','IT Manager',NULL,NULL,'REQUIRED','ACTIVE','2026-01-01','2026-01-01',now(),1)
ON CONFLICT (staff_code) DO UPDATE SET display_name=EXCLUDED.display_name, office_code=EXCLUDED.office_code, position_title=EXCLUDED.position_title, manager_employee_id=EXCLUDED.manager_employee_id, updated_at=now();

INSERT INTO peoplecore.employee_assignment(id,employee_id,company_code,department_code,office_code,position_title,manager_employee_id,timesheet_policy,effective_from,created_at,created_by) VALUES
('61111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111111','CPG-TRIAL','TRIAL-DESIGN','HN','Deputy General Manager (Hanoi Office) & IDD-BIM Manager',NULL,'REQUIRED','2026-01-01',now(),'TRIAL-SEED'),
('62222222-2222-4222-8222-222222222222','22222222-2222-4222-8222-222222222222','CPG-TRIAL','TRIAL-DESIGN','HN','AI Visualization Specialist','11111111-1111-4111-8111-111111111111','REQUIRED','2026-01-01',now(),'TRIAL-SEED'),
('63333333-3333-4333-8333-333333333333','33333333-3333-4333-8333-333333333333','CPG-TRIAL','TRIAL-HR','HCM','HR Manager',NULL,'REQUIRED','2026-01-01',now(),'TRIAL-SEED'),
('64444444-4444-4444-8444-444444444444','44444444-4444-4444-8444-444444444444','CPG-TRIAL','TRIAL-PAYROLL','HCM','Senior Manager - Finance & HR Admin',NULL,'REQUIRED','2026-01-01',now(),'TRIAL-SEED'),
('65555555-5555-4555-8555-555555555555','55555555-5555-4555-8555-555555555555','CPG-TRIAL','TRIAL-IT','HCM','IT Manager',NULL,'REQUIRED','2026-01-01',now(),'TRIAL-SEED')
ON CONFLICT (id) DO UPDATE SET office_code=EXCLUDED.office_code, position_title=EXCLUDED.position_title, manager_employee_id=EXCLUDED.manager_employee_id, timesheet_policy=EXCLUDED.timesheet_policy;

INSERT INTO peoplecore.employee_identity(id,employee_id,entra_tenant_id,entra_object_id,linked_email,is_active,linked_at) VALUES
('71111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111111','trial-local','81111111-1111-4111-8111-111111111111','trial-mgr@example.invalid',true,now()),
('72222222-2222-4222-8222-222222222222','22222222-2222-4222-8222-222222222222','trial-local','82222222-2222-4222-8222-222222222222','trial-emp@example.invalid',true,now()),
('73333333-3333-4333-8333-333333333333','33333333-3333-4333-8333-333333333333','trial-local','83333333-3333-4333-8333-333333333333','trial-hr@example.invalid',true,now()),
('74444444-4444-4444-8444-444444444444','44444444-4444-4444-8444-444444444444','trial-local','84444444-4444-4444-8444-444444444444','trial-pay@example.invalid',true,now()),
('75555555-5555-4555-8555-555555555555','55555555-5555-4555-8555-555555555555','trial-local','85555555-5555-4555-8555-555555555555','trial-admin@example.invalid',true,now())
ON CONFLICT (entra_tenant_id,entra_object_id) DO NOTHING;

INSERT INTO peoplecore.authorization_grant(id,employee_id,role_code,scope_type,scope_value,starts_at,granted_by,reason) VALUES
('91111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111111','MANAGER','SELF',NULL,'2026-01-01T00:00:00Z','TRIAL-SEED','LOCAL_TRIAL_FIXTURE'),
('93333333-3333-4333-8333-333333333333','33333333-3333-4333-8333-333333333333','HR','GLOBAL',NULL,'2026-01-01T00:00:00Z','TRIAL-SEED','LOCAL_TRIAL_FIXTURE'),
('94444444-4444-4444-8444-444444444444','44444444-4444-4444-8444-444444444444','PAYROLL','GLOBAL',NULL,'2026-01-01T00:00:00Z','TRIAL-SEED','LOCAL_TRIAL_FIXTURE'),
('95555555-5555-4555-8555-555555555555','55555555-5555-4555-8555-555555555555','ADMIN','GLOBAL',NULL,'2026-01-01T00:00:00Z','TRIAL-SEED','LOCAL_TRIAL_FIXTURE')
ON CONFLICT DO NOTHING;

WITH payload AS (
  SELECT '{"codes":[{"code":"DEMO-001","name":"Synthetic Trial Project","status":"ACTIVE","validFrom":"2026-01-01"}],"fixture":"LOCAL_TRIAL_NOT_BRAVO_EVIDENCE"}'::jsonb AS j
), inserted AS (
  INSERT INTO peoplecore.integration_message(id,direction,integration,message_type,idempotency_key,payload_json,schema_version,payload_sha256,external_reference,status,attempt_count,created_at,processed_at)
  SELECT 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','IN','BRAVO','PROJECT_CODE_V1','TRIAL-DEMO-PROJECT-CODE',j,'1.0',encode(digest(j::text,'sha256'),'hex'),'TRIAL-DEMO-NOT-BRAVO-EVIDENCE','PROCESSED',0,now(),now() FROM payload
  ON CONFLICT (idempotency_key) DO NOTHING
  RETURNING id
)
INSERT INTO peoplecore.project_code(code,name,status,valid_from,source_system,source_revision,last_source_message_id,synced_at)
SELECT 'DEMO-001','Synthetic Trial Project','ACTIVE','2026-01-01','BRAVO','TRIAL-DEMO-NOT-BRAVO-EVIDENCE',COALESCE((SELECT id FROM inserted),(SELECT id FROM peoplecore.integration_message WHERE idempotency_key='TRIAL-DEMO-PROJECT-CODE')),now()
ON CONFLICT (code) DO NOTHING;

INSERT INTO peoplecore.payroll_official_result(id,employee_id,payroll_period,source_system,source_run_id,components_json,imported_at,approved_at)
VALUES('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb','22222222-2222-4222-8222-222222222222','2026-08','BRAVO','TRIAL-DEMO-BRAVO-FIXTURE-NOT-EVIDENCE','{"DEMO_GROSS":1000.00,"DEMO_NET":900.00,"DEMO_NOTE":0.00}',now(),now())
ON CONFLICT (employee_id,payroll_period) DO NOTHING;

INSERT INTO peoplecore.performance_period(id,code,name,start_date,end_date,status,created_at,created_by)
VALUES('cccccccc-cccc-4ccc-8ccc-cccccccccccc','TRIAL-2026','Synthetic Trial Performance 2026','2026-01-01','2026-12-31','OPEN',now(),'TRIAL-SEED')
ON CONFLICT (code) DO NOTHING;

COMMIT;
