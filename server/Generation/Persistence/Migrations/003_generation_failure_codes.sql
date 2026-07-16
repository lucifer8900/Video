ALTER TABLE generation_jobs
    ADD COLUMN failure_code text;

UPDATE generation_jobs
SET failure_code = 'generation.failed'
WHERE status = 'failed' AND failure_code IS NULL;

ALTER TABLE generation_jobs
    ADD CONSTRAINT generation_jobs_failure_code_check
    CHECK
    (
        (status = 'failed' AND failure_code IN ('generation.failed', 'moderation.rejected'))
        OR
        (status <> 'failed' AND failure_code IS NULL)
    );
