# 回帰テストの移行対応表

基準: `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`。元の108関数を同名のC#テストへ移植した（現在は xUnit）。入力、期待値、内部の行列、負の条件を保持する。

`dotnet test tests/CobolCS.IntegrationTests --no-build` で実行する。元のソースは基準コミットのGit履歴で参照できる。解析器の51固定境界入力はこれらに加えて維持している。

探索workerの追加回帰は [`ProbeSessionTests`](../CobolCS.IntegrationTests/ProbeSessionTests.cs) の11件です。再利用と32試行での回収、timeout・EOF・異常JSON・文字／UTF-8バイト／stderr上限後の復旧、孫プロセスの終了、mutable static・readonlyな変更可能オブジェクトの毎試行初期化と共有可能なコードを検証します。生成・固定再生・adoptと所有権はBranchGenerationTestsの12件、全生成物の決定性はBackendScalingTestsの3件でも維持します。

| 元のテスト | C#側の対応 | 状態 |
| --- | --- | --- |
| `regression.py::Regression.test_remaining18_documentary_native_execution` | [`RegressionTests.TestRemaining18DocumentaryNativeExecution`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_remaining18_lifecycle_fails_at_named_stub` | [`RegressionTests.TestRemaining18LifecycleFailsAtNamedStub`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_remaining18_condition_selection_and_unset_stub` | [`RegressionTests.TestRemaining18ConditionSelectionAndUnsetStub`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_remaining18_missing_linkage_unknown_build` | [`RegressionTests.TestRemaining18MissingLinkageUnknownBuild`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_numeric_class_review` | [`RegressionTests.TestNumericClassReview`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_function_move_review` | [`RegressionTests.TestFunctionMoveReview`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_numeric_class_matrix_both_backends` | [`RegressionTests.TestNumericClassMatrixBothBackends`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_function_type_validation_matrix` | [`RegressionTests.TestFunctionTypeValidationMatrix`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_source_has_no_runtime_ir_and_is_portable` | [`RegressionTests.TestSourceHasNoRuntimeIrAndIsPortable`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_call_stub_replacement_and_regeneration` | [`RegressionTests.TestCallStubReplacementAndRegeneration`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_nested_source_temporaries` | [`RegressionTests.TestNestedSourceTemporaries`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_nested_call_response_and_reference_temporaries` | [`RegressionTests.TestNestedCallResponseAndReferenceTemporaries`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_not_on_size_error_only` | [`RegressionTests.TestNotOnSizeErrorOnly`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_edited_stub_reordering_is_refused_atomically` | [`RegressionTests.TestEditedStubReorderingIsRefusedAtomically`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_public_headers_numbered_paragraphs_and_filler` | [`RegressionTests.TestPublicHeadersNumberedParagraphsAndFiller`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_public_extensions_are_enumerated` | [`RegressionTests.TestPublicExtensionsAreEnumerated`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_no_parsed_program_still_produces_diagnostics` | [`RegressionTests.TestNoParsedProgramStillProducesDiagnostics`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_add_giving_without_to` | [`RegressionTests.TestAddGivingWithoutTo`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_ambiguous_qualified_fixture_names_do_not_break_build` | [`RegressionTests.TestAmbiguousQualifiedFixtureNamesDoNotBreakBuild`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unresolved_flow_and_header_stubs_have_unique_control_names` | [`RegressionTests.TestUnresolvedFlowAndHeaderStubsHaveUniqueControlNames`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_fixed_record_metadata_is_retained` | [`RegressionTests.TestFixedRecordMetadataIsRetained`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_procedure_using_comma_keeps_argument_order` | [`RegressionTests.TestProcedureUsingCommaKeepsArgumentOrder`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unreachable_stubs_and_program_isolation` | [`RegressionTests.TestUnreachableStubsAndProgramIsolation`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_sql_typed_stub_updates_and_undefined_response` | [`RegressionTests.TestSqlTypedStubUpdatesAndUndefinedResponse`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unsupported_c_to_f_are_editable_block_stubs` | [`RegressionTests.TestUnsupportedCToFAreEditableBlockStubs`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_typed_expression_stub_and_table_fixture` | [`RegressionTests.TestTypedExpressionStubAndTableFixture`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_file_contract_eof_status_and_trace` | [`RegressionTests.TestFileContractEofStatusAndTrace`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_sort_merge_are_isolated_stubs` | [`RegressionTests.TestSortMergeAreIsolatedStubs`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_accept_fixture_sequence_and_exhaustion` | [`RegressionTests.TestAcceptFixtureSequenceAndExhaustion`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_paragraph_fallthrough_thru_goto_and_size_branch` | [`RegressionTests.TestParagraphFallthroughThruGotoAndSizeBranch`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_names_groups_and_condition_properties` | [`RegressionTests.TestNamesGroupsAndConditionProperties`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unknown_control_block_does_not_leak` | [`RegressionTests.TestUnknownControlBlockDoesNotLeak`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_value_and_condition_bounds_are_validated` | [`RegressionTests.TestValueAndConditionBoundsAreValidated`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_evaluate_body_thru_remains_native` | [`RegressionTests.TestEvaluateBodyThruRemainsNative`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_call_reference_indices_are_captured_before_updates` | [`RegressionTests.TestCallReferenceIndicesAreCapturedBeforeUpdates`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_remainder_destination_index_is_captured` | [`RegressionTests.TestRemainderDestinationIndexIsCaptured`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_repeated_call_and_exception_branch` | [`RegressionTests.TestRepeatedCallAndExceptionBranch`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_control_stub_can_jump_to_sentence_or_paragraph` | [`RegressionTests.TestControlStubCanJumpToSentenceOrParagraph`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_reference_examples` | [`RegressionTests.TestReferenceExamples`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_strict_and_scaffold` | [`RegressionTests.TestStrictAndScaffold`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_deterministic_and_edit_protection` | [`RegressionTests.TestDeterministicAndEditProtection`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unmanaged_files_preserved` | [`RegressionTests.TestUnmanagedFilesPreserved`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_compare_and_invalid_options` | [`RegressionTests.TestCompareAndInvalidOptions`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_exact_decimal_and_negative_rounding` | [`RegressionTests.TestExactDecimalAndNegativeRounding`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_justified_right_truncation` | [`RegressionTests.TestJustifiedRightTruncation`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_division_alias_remainder` | [`RegressionTests.TestDivisionAliasRemainder`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_divide_by_zero_size_error` | [`RegressionTests.TestDivideByZeroSizeError`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_loop_and_exit` | [`RegressionTests.TestLoopAndExit`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_test_after` | [`RegressionTests.TestTestAfter`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_step_limit` | [`RegressionTests.TestStepLimit`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_goto_depending_and_section_perform` | [`RegressionTests.TestGotoDependingAndSectionPerform`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unsupported_profile` | [`RegressionTests.TestUnsupportedProfile`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_subscript_bounds` | [`RegressionTests.TestSubscriptBounds`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_fixture_and_reference_modification` | [`RegressionTests.TestFixtureAndReferenceModification`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_unknown_external_fixture_fails` | [`RegressionTests.TestUnknownExternalFixtureFails`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_missing_copy_scaffold` | [`RegressionTests.TestMissingCopyScaffold`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_redefines_and_group_moves_refused` | [`RegressionTests.TestRedefinesAndGroupMovesRefused`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_symlink_and_manifest_traversal_refused` | [`RegressionTests.TestSymlinkAndManifestTraversalRefused`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_standalone_multi_program` | [`RegressionTests.TestStandaloneMultiProgram`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `regression.py::Regression.test_generated_stylecop_is_active` | [`RegressionTests.TestGeneratedStylecopIsActive`](../CobolCS.IntegrationTests/Migrated/RegressionTests.cs) | C#へ移植 |
| `test_workflow_noop.py::Exploration.test_partial_trace_and_default_throw` | [`ExplorationTests.TestPartialTraceAndDefaultThrow`](../CobolCS.IntegrationTests/Migrated/ExplorationTests.cs) | C#へ移植 |
| `test_workflow_noop.py::Exploration.test_cancel_after_noop_retains_trace` | [`ExplorationTests.TestCancelAfterNoopRetainsTrace`](../CobolCS.IntegrationTests/Migrated/ExplorationTests.cs) | C#へ移植 |
| `test_workflow_external.py::ExternalWorkflow.test_file_and_sql` | [`ExternalWorkflowTests.TestFileAndSql`](../CobolCS.IntegrationTests/Migrated/ExternalWorkflowTests.cs) | C#へ移植 |
| `test_workflow_external.py::ExternalWorkflow.test_typed_setup_and_snapshot` | [`ExternalWorkflowTests.TestTypedSetupAndSnapshot`](../CobolCS.IntegrationTests/Migrated/ExternalWorkflowTests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_accept_terminator_preserves_next_statement` | [`Remaining18Tests.TestAcceptTerminatorPreservesNextStatement`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_unstring_comma_receivers_retain_metadata` | [`Remaining18Tests.TestUnstringCommaReceiversRetainMetadata`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_documentary_apostrophe` | [`Remaining18Tests.TestDocumentaryApostrophe`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_documentary_fixed_positions` | [`Remaining18Tests.TestDocumentaryFixedPositions`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_literal_escape_and_continuation` | [`Remaining18Tests.TestLiteralEscapeAndContinuation`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_unterminated_literal_does_not_swallow_next_line` | [`Remaining18Tests.TestUnterminatedLiteralDoesNotSwallowNextLine`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_program_attributes` | [`Remaining18Tests.TestProgramAttributes`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_program_attribute_rejections_and_end_name` | [`Remaining18Tests.TestProgramAttributeRejectionsAndEndName`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_fd_clauses` | [`Remaining18Tests.TestFdClauses`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_fixed_fd_orders_and_invalid_clauses` | [`Remaining18Tests.TestFixedFdOrdersAndInvalidClauses`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_fd_missing_period_preserves_boundary` | [`Remaining18Tests.TestFdMissingPeriodPreservesBoundary`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_missing_linkage_copy` | [`Remaining18Tests.TestMissingLinkageCopy`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_using_known_storage_and_unrelated_unknown_are_rejected` | [`Remaining18Tests.TestUsingKnownStorageAndUnrelatedUnknownAreRejected`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_provided_copy_replacing_and_ambiguity` | [`Remaining18Tests.TestProvidedCopyReplacingAndAmbiguity`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_unset_condition_retains_procedure` | [`Remaining18Tests.TestUnsetConditionRetainsProcedure`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_condition_selection_and_procedure_block` | [`Remaining18Tests.TestConditionSelectionAndProcedureBlock`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_nested_and_malformed_conditions` | [`Remaining18Tests.TestNestedAndMalformedConditions`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_unknown_declaration_recovery` | [`Remaining18Tests.TestUnknownDeclarationRecovery`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_selected_statement_is_not_marked_excluded` | [`Remaining18Tests.TestSelectedStatementIsNotMarkedExcluded`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_remaining18.py::Remaining18.test_conditional_children_keep_known_siblings_typed` | [`Remaining18Tests.TestConditionalChildrenKeepKnownSiblingsTyped`](../CobolCS.IntegrationTests/Migrated/Remaining18Tests.cs) | C#へ移植 |
| `test_workflow.py::Workflow.test_public_api_and_adoption` | [`WorkflowTests.TestPublicApiAndAdoption`](../CobolCS.IntegrationTests/Migrated/WorkflowTests.cs) | C#へ移植 |
| `test_workflow_safety.py::Ownership.test_ownership_and_paths` | [`OwnershipTests.TestOwnershipAndPaths`](../CobolCS.IntegrationTests/Migrated/OwnershipTests.cs) | C#へ移植 |
| `test_workflow_safety.py::Ownership.test_new_program_and_strict_registration` | [`OwnershipTests.TestNewProgramAndStrictRegistration`](../CobolCS.IntegrationTests/Migrated/OwnershipTests.cs) | C#へ移植 |
| `test_workflow_safety.py::Ownership.test_adoption_rejects_changed_or_skipped_case` | [`OwnershipTests.TestAdoptionRejectsChangedOrSkippedCase`](../CobolCS.IntegrationTests/Migrated/OwnershipTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_tamper_fails_before_output_is_created` | [`CorpusToolTests.TestTamperFailsBeforeOutputIsCreated`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_duplicate_ids_are_not_merged` | [`CorpusToolTests.TestDuplicateIdsAreNotMerged`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_gpl_requires_explicit_selection` | [`CorpusToolTests.TestGplRequiresExplicitSelection`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_paths_and_copy_hashes` | [`CorpusToolTests.TestPathsAndCopyHashes`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_symlink_escape` | [`CorpusToolTests.TestSymlinkEscape`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_timeout_is_not_success` | [`CorpusToolTests.TestTimeoutIsNotSuccess`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_verify_requires_adapter_failure_and_preservation_evidence` | [`CorpusToolTests.TestVerifyRequiresAdapterFailureAndPreservationEvidence`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_archive_preserves_bytes_and_checks_identity` | [`CorpusToolTests.TestArchivePreservesBytesAndChecksIdentity`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_archive_traversal_and_bad_checksum_are_atomic` | [`CorpusToolTests.TestArchiveTraversalAndBadChecksumAreAtomic`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_native_perform_evaluate_and_measurement_separation` | [`CorpusToolTests.TestNativePerformEvaluateAndMeasurementSeparation`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_external_scaffold_is_not_counted_as_execution` | [`CorpusToolTests.TestExternalScaffoldIsNotCountedAsExecution`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_public_corpus.py::CorpusTests.test_missing_linkage_recovery_is_separate_from_analysis` | [`CorpusToolTests.TestMissingLinkageRecoveryIsSeparateFromAnalysis`](../CobolCS.IntegrationTests/CorpusToolTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_arguments_adopt_extract_and_mutate` | [`ReadinessTests.TestArgumentsAdoptExtractAndMutate`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_group_call` | [`ReadinessTests.TestGroupCall`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_initialize_and_string` | [`ReadinessTests.TestInitializeAndString`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_scalar_arguments_and_input_only_calls` | [`ReadinessTests.TestScalarArgumentsAndInputOnlyCalls`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_string_flow_and_initialize_selection` | [`ReadinessTests.TestStringFlowAndInitializeSelection`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_group_content_is_input_only` | [`ReadinessTests.TestGroupContentIsInputOnly`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_unsupported_regions_and_aliases` | [`ReadinessTests.TestUnsupportedRegionsAndAliases`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |
| `test_execution_readiness.py::Readiness.test_reports_and_fresh_templates` | [`ReadinessTests.TestReportsAndFreshTemplates`](../CobolCS.IntegrationTests/Migrated/ReadinessTests.cs) | C#へ移植 |

追加の業務回帰は BusinessFourFixesTests に集約し、CI workflows-b で発見します。新規 factory の状態分離・破棄、実バイト SHA-256、prepare 診断と必須履歴、実 xUnit Theory/TRX・複数実行・adopt・採用後破壊を検証します。合成 hash fixture は実 COBOL 採取の証明とは扱いません。

業務支援追加: SourceCharacterTests は固定非重複・空白・pointer/overflow・厳密変換、BusinessAssistanceTests は mapping/正式引数/試験注入/実 xUnit/adopt/サービス置換、BusinessExternalScaffoldTests は typed FILE/SQL fake の EOF/no-data/省略更新と負の応答、CharacterGenerationTests は source/dispatcher/添字/文全体スタブ、InternalStubMetricsTests は現在の停止/古い証跡/物理COPY件数/未取得/旧実装保持。既存の未対応補完は payment-completion.cbl の BEFORE 句で維持し、元 payment.cbl は native 受入に使います。固定期待IR・既存corpus lockは更新していません。
