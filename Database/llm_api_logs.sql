CREATE TABLE `llm_api_logs` (
    `log_id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `log_error_code` INT DEFAULT NULL,
    `log_message` VARCHAR(50) NOT NULL,
    `antrian_id` INT UNSIGNED DEFAULT NULL,
    `api_key_id` INT UNSIGNED DEFAULT NULL,
    `log_created_at` TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
    `log_tokens_used` INT DEFAULT NULL,
    `log_batch_number` INT DEFAULT NULL,
    `log_total_batches` INT DEFAULT NULL,
    `log_error_count` INT DEFAULT NULL,
    `log_key_tokens_used` INT DEFAULT NULL,
    PRIMARY KEY (`log_id`)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci;
