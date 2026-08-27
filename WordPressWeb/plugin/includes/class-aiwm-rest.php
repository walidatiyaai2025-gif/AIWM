<?php
if (!defined('ABSPATH')) { exit; }

final class AIWM_Web_REST
{
    private static array $request_started = [];
    private const NS = 'aiwm/v1';
    private const MAX_PAGE_SIZE = 100;

    public static function boot(): void
    {
        add_action('rest_api_init', [self::class, 'register_routes']);
        add_filter('rest_request_before_callbacks', [self::class, 'mark_request_start'], 10, 3);
        add_filter('rest_post_dispatch', [self::class, 'record_request_timing'], 10, 3);
    }

    public static function register_routes(): void
    {
        self::route('/health', WP_REST_Server::READABLE, 'health');
        self::route('/dashboard', WP_REST_Server::READABLE, 'dashboard');
        self::route('/sites', WP_REST_Server::READABLE, 'list_sites', self::pagination_args());
        self::route('/sites', WP_REST_Server::CREATABLE, 'create_site', self::site_write_args(), true);
        self::route('/sites/(?P<id>\d+)', WP_REST_Server::READABLE, 'get_site', ['id' => ['validate_callback' => [self::class, 'positive_id']]]);
        self::route('/sites/(?P<id>\d+)', WP_REST_Server::EDITABLE, 'update_site', array_merge(['id' => ['validate_callback' => [self::class, 'positive_id']]], self::site_write_args(false)), true);
        self::route('/sites/(?P<id>\d+)', WP_REST_Server::DELETABLE, 'delete_site', ['id' => ['validate_callback' => [self::class, 'positive_id']]], true);
        self::route('/explorer', WP_REST_Server::READABLE, 'list_explorer', array_merge(self::pagination_args(), [
            'site_id' => ['required' => true, 'validate_callback' => [self::class, 'positive_id']],
            'resource_type' => ['sanitize_callback' => 'sanitize_key'],
        ]));
        self::route('/audits', WP_REST_Server::READABLE, 'list_audits', array_merge(self::pagination_args(), ['site_id' => ['validate_callback' => [self::class, 'positive_id']]]));
        self::route('/findings', WP_REST_Server::READABLE, 'list_findings', array_merge(self::pagination_args(), ['audit_id' => ['validate_callback' => [self::class, 'positive_id']]]));
        self::route('/suggested-changes', WP_REST_Server::READABLE, 'list_changes', array_merge(self::pagination_args(), ['site_id' => ['validate_callback' => [self::class, 'positive_id']], 'status' => ['sanitize_callback' => 'sanitize_key']]));
        self::route('/suggested-changes/(?P<id>\d+)/decision', WP_REST_Server::CREATABLE, 'decide_change', [
            'id' => ['required' => true, 'validate_callback' => [self::class, 'positive_id']],
            'decision' => ['required' => true, 'validate_callback' => fn($v) => in_array($v, ['approved','rejected'], true)],
            'note' => ['sanitize_callback' => 'sanitize_textarea_field'],
            'version_hash' => ['required' => true, 'sanitize_callback' => 'sanitize_text_field'],
        ], true);
        self::route('/executions', WP_REST_Server::CREATABLE, 'create_execution', [
            'suggested_change_id' => ['required' => true, 'validate_callback' => [self::class, 'positive_id']],
            'idempotency_key' => ['required' => true, 'sanitize_callback' => 'sanitize_text_field'],
        ], true);
        self::route('/executions', WP_REST_Server::READABLE, 'list_executions', array_merge(self::pagination_args(), ['site_id' => ['validate_callback' => [self::class, 'positive_id']]]));
        self::route('/jobs', WP_REST_Server::READABLE, 'list_jobs', array_merge(self::pagination_args(), ['status' => ['sanitize_callback' => 'sanitize_key']]));
        self::route('/jobs/(?P<id>\d+)', WP_REST_Server::READABLE, 'get_job', ['id' => ['validate_callback' => [self::class, 'positive_id']]]);
        self::route('/jobs/(?P<id>\d+)/cancel', WP_REST_Server::CREATABLE, 'cancel_job', ['id' => ['validate_callback' => [self::class, 'positive_id']]], true);
        self::route('/evidence', WP_REST_Server::READABLE, 'list_evidence', array_merge(self::pagination_args(), ['execution_id' => ['validate_callback' => [self::class, 'positive_id']]]));
        self::route('/receipts', WP_REST_Server::READABLE, 'list_receipts', array_merge(self::pagination_args(), ['execution_id' => ['validate_callback' => [self::class, 'positive_id']]]));
        self::route('/providers', WP_REST_Server::READABLE, 'list_providers');
        self::route('/providers/(?P<provider>[a-z0-9_-]+)', WP_REST_Server::EDITABLE, 'update_provider', [
            'provider' => ['required' => true, 'sanitize_callback' => 'sanitize_key'],
            'label' => ['sanitize_callback' => 'sanitize_text_field'], 'model' => ['sanitize_callback' => 'sanitize_text_field'],
            'endpoint' => ['sanitize_callback' => 'esc_url_raw'], 'secret' => ['sanitize_callback' => [self::class, 'raw_secret']],
        ], true);
        self::route('/activity', WP_REST_Server::READABLE, 'list_activity', array_merge(self::pagination_args(), ['site_id' => ['validate_callback' => [self::class, 'positive_id']]]));
    }

    private static function route(string $path, $methods, string $callback, array $args = [], bool $mutation = false): void
    {
        register_rest_route(self::NS, $path, [
            'methods' => $methods,
            'callback' => [self::class, $callback],
            'permission_callback' => $mutation ? [self::class, 'can_mutate'] : [self::class, 'can_read'],
            'args' => $args,
        ]);
    }

    public static function can_read(): bool { return AIWM_Web_Security::can_read(); }
    public static function can_mutate(WP_REST_Request $request) { return AIWM_Web_Security::can_mutate($request); }
    public static function positive_id($value): bool { return is_numeric($value) && (int) $value > 0; }
    public static function raw_secret($value): string { return is_scalar($value) ? trim((string) $value) : ''; }

    private static function pagination_args(): array
    {
        return [
            'page' => ['default' => 1, 'sanitize_callback' => 'absint'],
            'per_page' => ['default' => 25, 'sanitize_callback' => 'absint'],
        ];
    }

    private static function site_write_args(bool $required = true): array
    {
        return [
            'name' => ['required' => $required, 'sanitize_callback' => 'sanitize_text_field'],
            'base_url' => ['required' => $required, 'sanitize_callback' => 'esc_url_raw'],
            'auth_type' => ['default' => 'application_password', 'sanitize_callback' => 'sanitize_key'],
            'credential' => ['sanitize_callback' => [self::class, 'raw_secret']],
        ];
    }

    private static function page(WP_REST_Request $r): array
    {
        $page = max(1, (int) $r->get_param('page'));
        $per = min(self::MAX_PAGE_SIZE, max(1, (int) $r->get_param('per_page')));
        return [$page, $per, ($page - 1) * $per];
    }

    private static function list_response(array $rows, int $total, int $page, int $per): WP_REST_Response
    {
        $response = new WP_REST_Response(['items' => $rows, 'pagination' => ['page' => $page, 'perPage' => $per, 'total' => $total, 'pages' => (int) ceil($total / $per)]]);
        $response->header('X-WP-Total', (string) $total);
        $response->header('X-WP-TotalPages', (string) ceil($total / $per));
        return $response;
    }

    public static function health(): WP_REST_Response
    {
        global $wpdb;
        $required = ['sites','site_state','explorer_snapshots','seo_audits','findings','suggested_changes','approval_decisions','jobs','job_items','executions','evidence','receipts','ai_provider_config','ai_usage','activity_log'];
        $tables = [];
        foreach ($required as $name) {
            $full = AIWM_Web_Store::table($name);
            $tables[$name] = $wpdb->get_var($wpdb->prepare('SHOW TABLES LIKE %s', $full)) === $full;
        }
        return new WP_REST_Response(['ok' => !in_array(false, $tables, true), 'version' => AIWM_Web_Edition::VERSION, 'schemaVersion' => get_option('aiwm_web_schema_version'), 'tables' => $tables, 'queue' => function_exists('as_schedule_single_action') ? 'action-scheduler' : 'wp-cron', 'timestamp' => gmdate('c')]);
    }

    public static function dashboard(): WP_REST_Response { return new WP_REST_Response(AIWM_Web_Store::dashboard()); }

    public static function list_sites(WP_REST_Request $r): WP_REST_Response
    {
        global $wpdb; [$page,$per,$offset] = self::page($r); $t = AIWM_Web_Store::table('sites');
        $total = (int) $wpdb->get_var("SELECT COUNT(*) FROM {$t}");
        $rows = $wpdb->get_results($wpdb->prepare("SELECT id,name,base_url,status,auth_type,identity_fingerprint,last_verified_at,created_at,updated_at FROM {$t} ORDER BY updated_at DESC,id DESC LIMIT %d OFFSET %d", $per, $offset), ARRAY_A);
        return self::list_response($rows ?: [], $total, $page, $per);
    }

    public static function get_site(WP_REST_Request $r)
    {
        global $wpdb; $id=(int)$r['id']; $s=AIWM_Web_Store::table('sites'); $st=AIWM_Web_Store::table('site_state');
        $row=$wpdb->get_row($wpdb->prepare("SELECT s.id,s.name,s.base_url,s.status,s.auth_type,s.identity_fingerprint,s.last_verified_at,s.created_at,s.updated_at,ss.sync_status,ss.wp_version,ss.home_url,ss.content_hash,ss.last_synced_at FROM {$s} s LEFT JOIN {$st} ss ON ss.site_id=s.id WHERE s.id=%d",$id),ARRAY_A);
        return $row ? new WP_REST_Response($row) : new WP_Error('aiwm_not_found','Site not found.',['status'=>404]);
    }

    public static function create_site(WP_REST_Request $r)
    {
        global $wpdb; $url=trailingslashit(esc_url_raw((string)$r['base_url']));
        if (!$url || !wp_http_validate_url($url)) { return new WP_Error('aiwm_invalid_url','A valid HTTP(S) site URL is required.',['status'=>400]); }
        $credential_ref=null; $credential=(string)$r->get_param('credential');
        if ($credential!=='') { try { $credential_ref=AIWM_Web_Security::store_secret('site',$credential); } catch(Throwable $e){ return new WP_Error('aiwm_secret_store',$e->getMessage(),['status'=>500]); } }
        $now=AIWM_Web_Store::now(); $ok=$wpdb->insert(AIWM_Web_Store::table('sites'),['name'=>(string)$r['name'],'base_url'=>$url,'status'=>'pending','auth_type'=>(string)$r['auth_type'],'credential_ref'=>$credential_ref,'created_at'=>$now,'updated_at'=>$now]);
        if(!$ok){ if($credential_ref){AIWM_Web_Security::delete_secret($credential_ref);} return new WP_Error('aiwm_db_write','Unable to create site.',['status'=>500]); }
        $id=(int)$wpdb->insert_id; $wpdb->replace(AIWM_Web_Store::table('site_state'),['site_id'=>$id,'sync_status'=>'idle','updated_at'=>$now]);
        AIWM_Web_Store::invalidate_dashboard(); AIWM_Web_Store::audit('site','created',$id,'site',(string)$id,['base_url'=>$url]);
        return new WP_REST_Response(['id'=>$id,'status'=>'pending'],201);
    }

    public static function update_site(WP_REST_Request $r)
    {
        global $wpdb; $id=(int)$r['id']; $t=AIWM_Web_Store::table('sites'); $existing=$wpdb->get_row($wpdb->prepare("SELECT * FROM {$t} WHERE id=%d",$id),ARRAY_A);
        if(!$existing){return new WP_Error('aiwm_not_found','Site not found.',['status'=>404]);}
        $data=['updated_at'=>AIWM_Web_Store::now()];
        if($r->has_param('name')){$data['name']=(string)$r['name'];}
        if($r->has_param('base_url')){$url=trailingslashit(esc_url_raw((string)$r['base_url'])); if(!$url||!wp_http_validate_url($url)){return new WP_Error('aiwm_invalid_url','A valid HTTP(S) site URL is required.',['status'=>400]);} $data['base_url']=$url; $data['status']='pending'; $data['identity_fingerprint']=null; $data['last_verified_at']=null;}
        if($r->has_param('auth_type')){$data['auth_type']=(string)$r['auth_type'];}
        if((string)$r->get_param('credential')!==''){try{$data['credential_ref']=AIWM_Web_Security::replace_secret($existing['credential_ref']?:null,'site',(string)$r->get_param('credential'));}catch(Throwable $e){return new WP_Error('aiwm_secret_store',$e->getMessage(),['status'=>500]);}}
        $wpdb->update($t,$data,['id'=>$id]); AIWM_Web_Store::audit('site','updated',$id,'site',(string)$id,['fields'=>array_keys($data)]); return self::get_site($r);
    }

    public static function delete_site(WP_REST_Request $r)
    {
        global $wpdb; $id=(int)$r['id']; $t=AIWM_Web_Store::table('sites'); $row=$wpdb->get_row($wpdb->prepare("SELECT credential_ref FROM {$t} WHERE id=%d",$id),ARRAY_A);
        if(!$row){return new WP_Error('aiwm_not_found','Site not found.',['status'=>404]);}
        $active=(int)$wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM ".AIWM_Web_Store::table('jobs')." WHERE site_id=%d AND status IN ('queued','running','retry')",$id));
        if($active>0){return new WP_Error('aiwm_site_busy','Cancel or finish active jobs before removing this site.',['status'=>409]);}
        $wpdb->delete($t,['id'=>$id]); $wpdb->delete(AIWM_Web_Store::table('site_state'),['site_id'=>$id]); if($row['credential_ref']){AIWM_Web_Security::delete_secret($row['credential_ref']);}
        AIWM_Web_Store::invalidate_dashboard(); AIWM_Web_Store::audit('site','deleted',$id,'site',(string)$id); return new WP_REST_Response(['deleted'=>true,'id'=>$id]);
    }

    public static function list_explorer(WP_REST_Request $r): WP_REST_Response
    {
        global $wpdb; [$page,$per,$offset]=self::page($r); $t=AIWM_Web_Store::table('explorer_snapshots'); $site=(int)$r['site_id']; $type=(string)$r->get_param('resource_type');
        $where='site_id=%d'; $args=[$site]; if($type!==''){$where.=' AND resource_type=%s';$args[]=$type;}
        $total=(int)$wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$t} WHERE {$where}",...$args)); $args[]=$per; $args[]=$offset;
        $rows=$wpdb->get_results($wpdb->prepare("SELECT id,site_id,resource_type,resource_id,resource_version,payload_hash,captured_at FROM {$t} WHERE {$where} ORDER BY captured_at DESC,id DESC LIMIT %d OFFSET %d",...$args),ARRAY_A);
        return self::list_response($rows?:[],$total,$page,$per);
    }

    private static function simple_list(WP_REST_Request $r,string $table,string $select,string $order,string $filter_col='',string $filter_param=''): WP_REST_Response
    {
        global $wpdb; [$page,$per,$offset]=self::page($r); $t=AIWM_Web_Store::table($table); $where='1=1'; $args=[];
        if($filter_col && $r->get_param($filter_param)!==null && $r->get_param($filter_param)!==''){$where.=" AND {$filter_col}=%s";$args[]=(string)$r->get_param($filter_param);}
        $total=(int)$wpdb->get_var($args?$wpdb->prepare("SELECT COUNT(*) FROM {$t} WHERE {$where}",...$args):"SELECT COUNT(*) FROM {$t} WHERE {$where}"); $args[]=$per;$args[]=$offset;
        $rows=$wpdb->get_results($wpdb->prepare("SELECT {$select} FROM {$t} WHERE {$where} ORDER BY {$order} LIMIT %d OFFSET %d",...$args),ARRAY_A);
        return self::list_response($rows?:[],$total,$page,$per);
    }

    public static function list_audits(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'seo_audits','id,site_id,status,score,findings_count,job_id,started_at,completed_at,created_at','created_at DESC,id DESC','site_id','site_id'); }
    public static function list_findings(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'findings','id,site_id,audit_id,severity,rule_key,object_type,object_id,summary,created_at','id DESC','audit_id','audit_id'); }

    public static function list_changes(WP_REST_Request $r): WP_REST_Response
    {
        global $wpdb; [$page,$per,$offset]=self::page($r); $t=AIWM_Web_Store::table('suggested_changes'); $where=['1=1'];$args=[];
        if($r->get_param('site_id')){$where[]='site_id=%d';$args[]=(int)$r->get_param('site_id');} if($r->get_param('status')){$where[]='status=%s';$args[]=(string)$r->get_param('status');}
        $w=implode(' AND ',$where); $total=(int)$wpdb->get_var($args?$wpdb->prepare("SELECT COUNT(*) FROM {$t} WHERE {$w}",...$args):"SELECT COUNT(*) FROM {$t} WHERE {$w}"); $args[]=$per;$args[]=$offset;
        $rows=$wpdb->get_results($wpdb->prepare("SELECT id,site_id,audit_id,finding_id,object_type,object_id,risk,status,before_json,proposed_json,version_hash,created_at,updated_at FROM {$t} WHERE {$w} ORDER BY updated_at DESC,id DESC LIMIT %d OFFSET %d",...$args),ARRAY_A);
        foreach($rows as &$row){$row['before']=AIWM_Web_Store::decode($row['before_json']);$row['proposed']=AIWM_Web_Store::decode($row['proposed_json']);unset($row['before_json'],$row['proposed_json']);}
        return self::list_response($rows?:[],$total,$page,$per);
    }

    public static function decide_change(WP_REST_Request $r)
    {
        global $wpdb; $id=(int)$r['id']; $changes=AIWM_Web_Store::table('suggested_changes'); $row=$wpdb->get_row($wpdb->prepare("SELECT * FROM {$changes} WHERE id=%d",$id),ARRAY_A);
        if(!$row){return new WP_Error('aiwm_not_found','Suggested change not found.',['status'=>404]);}
        if(!hash_equals((string)$row['version_hash'],(string)$r['version_hash'])){return new WP_Error('aiwm_stale_change','The suggested change changed after it was loaded. Refresh before deciding.',['status'=>409]);}
        $decision=(string)$r['decision']; $wpdb->insert(AIWM_Web_Store::table('approval_decisions'),['suggested_change_id'=>$id,'site_id'=>(int)$row['site_id'],'decision'=>$decision,'decision_version_hash'=>$row['version_hash'],'decided_by'=>get_current_user_id(),'note'=>(string)$r->get_param('note'),'created_at'=>AIWM_Web_Store::now()]);
        $decision_id=(int)$wpdb->insert_id; $wpdb->update($changes,['status'=>$decision,'updated_at'=>AIWM_Web_Store::now()],['id'=>$id]); AIWM_Web_Store::invalidate_dashboard(); AIWM_Web_Store::audit('approval',$decision,(int)$row['site_id'],'suggested_change',(string)$id,['decision_id'=>$decision_id]);
        return new WP_REST_Response(['id'=>$decision_id,'suggestedChangeId'=>$id,'decision'=>$decision],201);
    }

    public static function create_execution(WP_REST_Request $r)
    {
        global $wpdb; $change_id=(int)$r['suggested_change_id']; $key=trim((string)$r['idempotency_key']); if(strlen($key)<8||strlen($key)>190){return new WP_Error('aiwm_bad_idempotency','Idempotency key must be 8-190 characters.',['status'=>400]);}
        $executions=AIWM_Web_Store::table('executions'); $existing=$wpdb->get_row($wpdb->prepare("SELECT id,job_id,status FROM {$executions} WHERE idempotency_key=%s",$key),ARRAY_A); if($existing){return new WP_REST_Response($existing,200);}
        $change=$wpdb->get_row($wpdb->prepare("SELECT * FROM ".AIWM_Web_Store::table('suggested_changes')." WHERE id=%d",$change_id),ARRAY_A); if(!$change){return new WP_Error('aiwm_not_found','Suggested change not found.',['status'=>404]);}
        $approval=$wpdb->get_row($wpdb->prepare("SELECT * FROM ".AIWM_Web_Store::table('approval_decisions')." WHERE suggested_change_id=%d AND decision='approved' ORDER BY id DESC LIMIT 1",$change_id),ARRAY_A);
        if(!$approval||!hash_equals((string)$approval['decision_version_hash'],(string)$change['version_hash'])){return new WP_Error('aiwm_not_approved','A current approval is required before execution.',['status'=>409]);}
        $site=$wpdb->get_row($wpdb->prepare("SELECT id,status,identity_fingerprint FROM ".AIWM_Web_Store::table('sites')." WHERE id=%d",$change['site_id']),ARRAY_A); if(!$site||$site['status']!=='verified'||empty($site['identity_fingerprint'])){return new WP_Error('aiwm_site_unverified','Target site identity must be verified before execution.',['status'=>409]);}
        if(empty($change['before_json'])){return new WP_Error('aiwm_missing_before','Before-state evidence is required before execution.',['status'=>409]);}
        $wpdb->insert($executions,['site_id'=>(int)$site['id'],'suggested_change_id'=>$change_id,'approval_decision_id'=>(int)$approval['id'],'idempotency_key'=>$key,'status'=>'queued','target_identity_hash'=>$site['identity_fingerprint'],'before_json'=>$change['before_json'],'created_by'=>get_current_user_id(),'created_at'=>AIWM_Web_Store::now()]);
        if(!$wpdb->insert_id){return new WP_Error('aiwm_db_write','Unable to create execution.',['status'=>500]);}
        $execution_id=(int)$wpdb->insert_id; AIWM_Web_Store::add_evidence((int)$site['id'],$execution_id,'before_state',AIWM_Web_Store::decode($change['before_json']),['change_id'=>$change_id,'version_hash'=>$change['version_hash']]);
        try{$job_id=AIWM_Web_Jobs::enqueue('execution',(int)$site['id'],['execution_id'=>$execution_id],'execution:'.$key,1);}catch(Throwable $e){$wpdb->update($executions,['status'=>'failed','error_json'=>AIWM_Web_Store::json(['code'=>'enqueue_failed','message'=>$e->getMessage()]),'completed_at'=>AIWM_Web_Store::now()],['id'=>$execution_id]);return new WP_Error('aiwm_enqueue_failed',$e->getMessage(),['status'=>500]);}
        $wpdb->update($executions,['job_id'=>$job_id],['id'=>$execution_id]); AIWM_Web_Store::audit('execution','queued',(int)$site['id'],'execution',(string)$execution_id,['job_id'=>$job_id]);
        return new WP_REST_Response(['id'=>$execution_id,'jobId'=>$job_id,'status'=>'queued'],202);
    }

    public static function list_executions(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'executions','id,site_id,suggested_change_id,approval_decision_id,job_id,idempotency_key,status,target_identity_hash,verification_json,error_json,created_by,created_at,completed_at','created_at DESC,id DESC','site_id','site_id'); }
    public static function list_jobs(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'jobs','id,site_id,type,status,idempotency_key,progress_current,progress_total,attempts,max_attempts,next_attempt_at,cancel_requested_at,created_at,started_at,finished_at','created_at DESC,id DESC','status','status'); }

    public static function get_job(WP_REST_Request $r)
    {
        global $wpdb; $row=$wpdb->get_row($wpdb->prepare("SELECT id,site_id,type,status,idempotency_key,progress_current,progress_total,cursor_json,error_json,attempts,max_attempts,next_attempt_at,cancel_requested_at,created_at,started_at,finished_at FROM ".AIWM_Web_Store::table('jobs')." WHERE id=%d",(int)$r['id']),ARRAY_A);
        if(!$row){return new WP_Error('aiwm_not_found','Job not found.',['status'=>404]);} $row['cursor']=AIWM_Web_Store::decode($row['cursor_json']);$row['error']=AIWM_Web_Store::decode($row['error_json'],null);unset($row['cursor_json'],$row['error_json']); return new WP_REST_Response($row);
    }

    public static function cancel_job(WP_REST_Request $r)
    {
        $id=(int)$r['id']; if(!AIWM_Web_Jobs::cancel($id)){return new WP_Error('aiwm_not_cancellable','Job is missing or no longer cancellable.',['status'=>409]);} AIWM_Web_Store::audit('job','cancel_requested',null,'job',(string)$id); return new WP_REST_Response(['id'=>$id,'cancelRequested'=>true],202);
    }

    public static function list_evidence(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'evidence','id,site_id,execution_id,kind,sha256,metadata_json,created_at','created_at DESC,id DESC','execution_id','execution_id'); }
    public static function list_receipts(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'receipts','id,execution_id,site_id,status,receipt_uuid,before_hash,after_hash,evidence_hash,summary_json,created_at','created_at DESC,id DESC','execution_id','execution_id'); }

    public static function list_providers(): WP_REST_Response
    {
        global $wpdb; $rows=$wpdb->get_results("SELECT provider_key,label,status,model,endpoint,(credential_ref IS NOT NULL) AS has_credential,config_json,updated_at FROM ".AIWM_Web_Store::table('ai_provider_config')." ORDER BY provider_key",ARRAY_A); return new WP_REST_Response(['items'=>$rows?:[]]);
    }

    public static function update_provider(WP_REST_Request $r)
    {
        global $wpdb; $key=(string)$r['provider']; $t=AIWM_Web_Store::table('ai_provider_config'); $old=$wpdb->get_row($wpdb->prepare("SELECT * FROM {$t} WHERE provider_key=%s",$key),ARRAY_A); $ref=$old['credential_ref']??null;
        $secret=(string)$r->get_param('secret'); if($secret!==''){try{$ref=AIWM_Web_Security::replace_secret($ref,'provider_'.$key,$secret);}catch(Throwable $e){return new WP_Error('aiwm_secret_store',$e->getMessage(),['status'=>500]);}}
        $data=['provider_key'=>$key,'label'=>(string)($r->get_param('label')?:($old['label']??ucfirst($key))),'status'=>$ref?'configured':'unconfigured','model'=>$r->has_param('model')?(string)$r['model']:($old['model']??null),'endpoint'=>$r->has_param('endpoint')?(string)$r['endpoint']:($old['endpoint']??null),'credential_ref'=>$ref,'config_json'=>$old['config_json']??AIWM_Web_Store::json([]),'updated_by'=>get_current_user_id(),'updated_at'=>AIWM_Web_Store::now()];
        $wpdb->replace($t,$data); AIWM_Web_Store::audit('provider','configured',null,'provider',$key,['has_credential'=>(bool)$ref]); return new WP_REST_Response(['provider'=>$key,'status'=>$data['status'],'hasCredential'=>(bool)$ref]);
    }

    public static function list_activity(WP_REST_Request $r): WP_REST_Response { return self::simple_list($r,'activity_log','id,site_id,actor_user_id,category,action,object_type,object_id,duration_ms,context_json,created_at','created_at DESC,id DESC','site_id','site_id'); }

    public static function mark_request_start($response, $handler, WP_REST_Request $request)
    {
        if (strpos($request->get_route(), '/' . self::NS . '/') === 0) { self::$request_started[spl_object_id($request)] = microtime(true); }
        return $response;
    }

    public static function record_request_timing($response, $server, WP_REST_Request $request)
    {
        $route=$request->get_route(); if(strpos($route,'/'.self::NS.'/')!==0){return $response;}
        $id=spl_object_id($request); $start=self::$request_started[$id]??null; unset(self::$request_started[$id]);
        $duration=$start?(int)round((microtime(true)-$start)*1000):null;
        AIWM_Web_Store::audit('performance','rest_dispatch',null,'route',$route,['method'=>$request->get_method()],$duration);
        if($response instanceof WP_HTTP_Response && $duration!==null){$response->header('X-AIWM-Duration-Ms',(string)$duration);}
        return $response;
    }
}
