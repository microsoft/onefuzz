// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{machine_id::MachineIdentity, sha256::digest_file_blocking};
use anyhow::{format_err, Context, Result};
use onefuzz_telemetry::{InstanceTelemetryKey, MicrosoftTelemetryKey};
use std::path::{Path, PathBuf};
use std::{collections::HashMap, hash::Hash};
use strum::IntoEnumIterator;
use strum_macros::EnumIter;
use uuid::Uuid;

pub enum ExpandedValue<'a> {
    Path(String),
    Scalar(String),
    List(&'a [String]),
    Mapping(MappingFn<'a>),
}

type MappingFn<'a> = Box<dyn Fn(&Expand<'a>, &str) -> Result<Option<ExpandedValue<'a>>> + Send>;

#[derive(PartialEq, Eq, Hash, EnumIter)]
pub enum PlaceHolder {
    Input,
    Crashes,
    InputCorpus,
    GeneratedInputs,
    TargetExe,
    TargetOptions,
    AnalyzerExe,
    AnalyzerOptions,
    OutputDir,
    InputFileNameNoExt,
    InputFileName,
    RuntimeDir,
    ToolsDir,
    CoverageDir,
    GeneratorExe,
    GeneratorOptions,
    SupervisorExe,
    SupervisorOptions,
    SetupDir,
    ReportsDir,
    JobId,
    TaskId,
    MachineId,
    CrashesContainer,
    CrashesAccount,
    MicrosoftTelemetryKey,
    InstanceTelemetryKey,
    InputFileSha256,
}

impl PlaceHolder {
    pub fn get_string(&self) -> &'static str {
        match self {
            Self::Input => "{input}",
            Self::Crashes => "{crashes}",
            Self::InputCorpus => "{input_corpus}",
            Self::GeneratedInputs => "{generated_inputs}",
            Self::TargetExe => "{target_exe}",
            Self::TargetOptions => "{target_options}",
            Self::AnalyzerExe => "{tool_exe}",
            Self::AnalyzerOptions => "{tool_options}",
            Self::OutputDir => "{output_dir}",
            Self::InputFileNameNoExt => "{input_file_name_no_ext}",
            Self::InputFileName => "{input_file_name}",
            Self::RuntimeDir => "{runtime_dir}",
            Self::ToolsDir => "{tools_dir}",
            Self::CoverageDir => "{coverage_dir}",
            Self::GeneratorExe => "{generator_exe}",
            Self::GeneratorOptions => "{generator_options}",
            Self::SupervisorExe => "{supervisor_exe}",
            Self::SupervisorOptions => "{supervisor_options}",
            Self::SetupDir => "{setup_dir}",
            Self::ReportsDir => "{reports_dir}",
            Self::JobId => "{job_id}",
            Self::TaskId => "{task_id}",
            Self::MachineId => "{machine_id}",
            Self::CrashesContainer => "{crashes_container}",
            Self::CrashesAccount => "{crashes_account}",
            Self::MicrosoftTelemetryKey => "{microsoft_telemetry_key}",
            Self::InstanceTelemetryKey => "{instance_telemetry_key}",
            Self::InputFileSha256 => "{input_file_sha256}",
        }
    }
}

pub struct Expand<'a> {
    values: HashMap<&'static str, ExpandedValue<'a>>,
    machine_identity: &'a MachineIdentity,
}

impl<'a> Expand<'a> {
    pub fn new(machine_identity: &'a MachineIdentity) -> Self {
        let mut values = HashMap::new();
        values.insert(
            PlaceHolder::InputFileNameNoExt.get_string(),
            ExpandedValue::Mapping(Box::new(Expand::extract_file_name_no_ext)),
        );
        values.insert(
            PlaceHolder::InputFileName.get_string(),
            ExpandedValue::Mapping(Box::new(Expand::extract_file_name)),
        );
        values.insert(
            PlaceHolder::InputFileSha256.get_string(),
            ExpandedValue::Mapping(Box::new(Expand::input_file_sha256)),
        );

        Self {
            values,
            machine_identity,
        }
    }

    // Must be manually called to enable the use of async library code.
    pub async fn machine_id(self) -> Result<Expand<'a>> {
        let id = self.machine_identity.machine_id;
        let value = id.to_string();
        Ok(self.set_value(PlaceHolder::MachineId, ExpandedValue::Scalar(value)))
    }

    fn input_file_sha256(&self, _format_str: &str) -> Result<Option<ExpandedValue<'a>>> {
        let val = match self.values.get(PlaceHolder::Input.get_string()) {
            Some(ExpandedValue::Path(fp)) => {
                let file = PathBuf::from(fp);
                let hash = digest_file_blocking(file)?;
                Some(ExpandedValue::Scalar(hash))
            }
            _ => None,
        };

        Ok(val)
    }

    fn extract_file_name_no_ext(&self, _format_str: &str) -> Result<Option<ExpandedValue<'a>>> {
        let val = match self.values.get(PlaceHolder::Input.get_string()) {
            Some(ExpandedValue::Path(fp)) => {
                let file = PathBuf::from(fp);
                let stem = file
                    .file_stem()
                    .ok_or_else(|| format_err!("missing file stem: {}", file.display()))?;
                let name_as_str = stem.to_string_lossy().to_string();
                Some(ExpandedValue::Scalar(name_as_str))
            }
            _ => None,
        };

        Ok(val)
    }

    fn extract_file_name(&self, _format_str: &str) -> Result<Option<ExpandedValue<'a>>> {
        let val = match self.values.get(PlaceHolder::Input.get_string()) {
            Some(ExpandedValue::Path(fp)) => {
                let file = PathBuf::from(fp);
                let name = file
                    .file_name()
                    .ok_or_else(|| format_err!("missing file name: {}", file.display()))?;
                let name_as_str = name.to_string_lossy().to_string();
                Some(ExpandedValue::Scalar(name_as_str))
            }
            _ => None,
        };

        Ok(val)
    }

    pub fn set_value(self, name: PlaceHolder, value: ExpandedValue<'a>) -> Self {
        let mut values = self.values;
        values.insert(name.get_string(), value);
        Self {
            values,
            machine_identity: self.machine_identity,
        }
    }

    pub fn set_optional_ref<'l, T: 'l>(
        self,
        value: &'l Option<T>,
        setter: impl FnOnce(Self, &'l T) -> Self,
    ) -> Self {
        if let Some(value) = value {
            setter(self, value)
        } else {
            self
        }
    }

    pub fn set_optional<T>(self, value: Option<T>, setter: impl FnOnce(Self, T) -> Self) -> Self {
        if let Some(value) = value {
            setter(self, value)
        } else {
            self
        }
    }

    pub fn generated_inputs(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::GeneratedInputs, ExpandedValue::Path(path))
    }

    pub fn crashes(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::Crashes, ExpandedValue::Path(path))
    }

    pub fn input_path(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::Input, ExpandedValue::Path(path))
    }

    pub fn input_marker(self, arg: &str) -> Self {
        self.set_value(PlaceHolder::Input, ExpandedValue::Scalar(String::from(arg)))
    }

    pub fn input_corpus(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::InputCorpus, ExpandedValue::Path(path))
    }

    pub fn generator_exe(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::GeneratorExe, ExpandedValue::Path(path))
    }

    pub fn generator_options(self, arg: &'a [String]) -> Self {
        self.set_value(PlaceHolder::GeneratorOptions, ExpandedValue::List(arg))
    }

    pub fn target_exe(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::TargetExe, ExpandedValue::Path(path))
    }

    pub fn target_options(self, arg: &'a [String]) -> Self {
        self.set_value(PlaceHolder::TargetOptions, ExpandedValue::List(arg))
    }

    pub fn analyzer_exe(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::AnalyzerExe, ExpandedValue::Path(path))
    }

    pub fn analyzer_options(self, arg: &'a [String]) -> Self {
        self.set_value(PlaceHolder::AnalyzerOptions, ExpandedValue::List(arg))
    }

    pub fn supervisor_exe(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::SupervisorExe, ExpandedValue::Path(path))
    }

    pub fn supervisor_options(self, arg: &'a [String]) -> Self {
        self.set_value(PlaceHolder::SupervisorOptions, ExpandedValue::List(arg))
    }

    pub fn output_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::OutputDir, ExpandedValue::Path(path))
    }

    pub fn reports_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::ReportsDir, ExpandedValue::Path(path))
    }

    pub fn tools_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::ToolsDir, ExpandedValue::Path(path))
    }

    pub fn runtime_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::RuntimeDir, ExpandedValue::Path(path))
    }

    pub fn setup_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::SetupDir, ExpandedValue::Path(path))
    }

    pub fn coverage_dir(self, arg: impl AsRef<Path>) -> Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::CoverageDir, ExpandedValue::Path(path))
    }

    pub fn task_id(self, arg: &Uuid) -> Self {
        let value = arg.to_hyphenated().to_string();
        self.set_value(PlaceHolder::TaskId, ExpandedValue::Scalar(value))
    }

    pub fn job_id(self, arg: &Uuid) -> Self {
        let value = arg.to_hyphenated().to_string();
        self.set_value(PlaceHolder::JobId, ExpandedValue::Scalar(value))
    }

    pub fn microsoft_telemetry_key(self, arg: &MicrosoftTelemetryKey) -> Self {
        let value = arg.to_string();
        self.set_value(
            PlaceHolder::MicrosoftTelemetryKey,
            ExpandedValue::Scalar(value),
        )
    }

    pub fn instance_telemetry_key(self, arg: &InstanceTelemetryKey) -> Self {
        let value = arg.to_string();
        self.set_value(
            PlaceHolder::InstanceTelemetryKey,
            ExpandedValue::Scalar(value),
        )
    }

    pub fn crashes_account(self, arg: &str) -> Self {
        self.set_value(
            PlaceHolder::CrashesAccount,
            ExpandedValue::Scalar(String::from(arg)),
        )
    }

    pub fn crashes_container(self, arg: &str) -> Self {
        self.set_value(
            PlaceHolder::CrashesContainer,
            ExpandedValue::Scalar(String::from(arg)),
        )
    }

    fn replace_value(
        &self,
        fmtstr: &str,
        mut arg: String,
        ev: &ExpandedValue<'a>,
        eval_stack: &mut Vec<&str>,
    ) -> Result<String> {
        match ev {
            ExpandedValue::Path(v) => {
                // evaluate the inner replacement first,
                // since it may in turn have replacements to be performed
                let evaluated = self.evaluate_value_checked(v, eval_stack)?;
                let path = dunce::canonicalize(evaluated)
                    .with_context(|| format!("unable to canonicalize path during extension: {v}"))?
                    .to_string_lossy()
                    .to_string();
                arg = arg.replace(fmtstr, &path);
                Ok(arg)
            }
            ExpandedValue::Scalar(v) => {
                arg = arg.replace(fmtstr, v);
                Ok(arg)
            }
            ExpandedValue::List(value) => {
                let replaced = self.evaluate_checked(value, eval_stack)?;
                let replaced = replaced.join(" ");
                arg = arg.replace(fmtstr, &replaced);
                Ok(arg)
            }
            ExpandedValue::Mapping(func) => {
                if let Some(value) = func(self, fmtstr)? {
                    let arg = self.replace_value(fmtstr, arg, &value, eval_stack)?;
                    Ok(arg)
                } else {
                    Ok(arg)
                }
            }
        }
    }

    fn evaluate_value_checked(
        &self,
        arg: impl AsRef<str>,
        eval_stack: &mut Vec<&str>,
    ) -> Result<String> {
        let mut arg = arg.as_ref().to_owned();
        for placeholder in PlaceHolder::iter() {
            let fmtstr = placeholder.get_string();
            match (arg.contains(fmtstr), self.values.get(fmtstr)) {
                (true, Some(ev)) => {
                    if eval_stack.contains(&fmtstr) {
                        eval_stack.push(fmtstr);
                        let path = eval_stack.join("->");
                        bail!(
                            "attempting to replace {fmtstr} with a value that contains itself (replacements {path})"
                        );
                    }

                    eval_stack.push(fmtstr);
                    arg = self
                        .replace_value(fmtstr, arg.clone(), ev, eval_stack)
                        .with_context(|| format!("replace_value failed: {fmtstr} {arg}"))?;
                    eval_stack.pop();
                }
                (true, None) => bail!("missing argument {}", fmtstr),
                (false, _) => (),
            }
        }

        Ok(arg)
    }

    pub fn evaluate_value(&self, arg: impl AsRef<str>) -> Result<String> {
        self.evaluate_value_checked(arg, &mut Vec::new())
    }

    fn evaluate_checked(
        &self,
        args: &[impl AsRef<str>],
        eval_stack: &mut Vec<&str>,
    ) -> Result<Vec<String>> {
        let mut result = Vec::new();
        for arg in args {
            let arg = self
                .evaluate_value_checked(arg, eval_stack)
                .with_context(|| format!("evaluating argument failed: {}", arg.as_ref()))?;
            result.push(arg);
        }
        Ok(result)
    }

    pub fn evaluate(&self, args: &[impl AsRef<str>]) -> Result<Vec<String>> {
        self.evaluate_checked(args, &mut Vec::new())
    }
}

#[cfg(test)]
mod tests {
    use crate::machine_id::MachineIdentity;

    use super::Expand;
    use anyhow::{Context, Result};
    use pretty_assertions::assert_eq;
    use std::path::Path;
    use uuid::Uuid;

    fn test_machine_identity() -> MachineIdentity {
        MachineIdentity {
            machine_id: Uuid::new_v4(),
            machine_name: "test-machine".to_string(),
            scaleset_name: None,
        }
    }

    #[test]
    fn test_setup_dir_and_target_exe() -> Result<()> {
        // use current exe name here, since path must exist:
        let current_exe = std::env::current_exe()?;
        let dir_part = current_exe.parent().unwrap();
        let name_part = current_exe.file_name().unwrap().to_string_lossy();

        let target_exe = Expand::new(&test_machine_identity())
            .setup_dir(dir_part)
            .target_exe(format!("{{setup_dir}}/{name_part}"))
            .evaluate_value("{target_exe}")?;

        assert_eq!(target_exe, current_exe.to_string_lossy());

        Ok(())
    }

    #[test]
    fn test_expand_nested() -> Result<()> {
        let result = Expand::new(&test_machine_identity())
            .target_options(&["a".to_string(), "b".to_string(), "c".to_string()])
            .supervisor_options(&["{target_options}".to_string()])
            .evaluate(&["{supervisor_options}"])?;
        assert_eq!(result, vec!["a b c"]);
        Ok(())
    }

    #[test]
    fn test_expand_nested_reverse() -> Result<()> {
        let result = Expand::new(&test_machine_identity())
            .supervisor_options(&["a".to_string(), "b".to_string(), "c".to_string()])
            .target_options(&["{supervisor_options}".to_string()])
            .evaluate(&["{target_options}"])?;
        assert_eq!(result, vec!["a b c"]);
        Ok(())
    }

    #[test]
    fn test_self_referential_list() -> Result<()> {
        let result = Expand::new(&test_machine_identity())
            .supervisor_options(&["{supervisor_options}".to_string()])
            .evaluate(&["{supervisor_options}"]);

        let e = result.err().unwrap();
        assert_eq!(
            format!("{e:#}"),
            "evaluating argument failed: {supervisor_options}: \
            replace_value failed: {supervisor_options} {supervisor_options}: \
            evaluating argument failed: {supervisor_options}: \
            attempting to replace {supervisor_options} with a value that contains itself \
            (replacements {supervisor_options}->{supervisor_options})"
        );
        Ok(())
    }

    #[test]
    fn test_self_referential_path() {
        let result = Expand::new(&test_machine_identity())
            .target_exe("{target_exe}")
            .evaluate(&["{target_exe}"]);

        let e = result.err().unwrap();
        assert_eq!(
            format!("{e:#}"),
            "evaluating argument failed: {target_exe}: \
            replace_value failed: {target_exe} {target_exe}: \
            attempting to replace {target_exe} with a value that contains itself \
            (replacements {target_exe}->{target_exe})"
        );
    }

    #[test]
    fn test_mutually_recursive() {
        let result = Expand::new(&test_machine_identity())
            .supervisor_options(&["{target_exe}".to_string()])
            .target_exe("{supervisor_options}")
            .evaluate(&["{target_exe}"]);

        let e = result.err().unwrap();
        assert_eq!(
            format!("{e:#}"),
            "evaluating argument failed: {target_exe}: \
            replace_value failed: {target_exe} {target_exe}: \
            replace_value failed: {supervisor_options} {supervisor_options}: \
            evaluating argument failed: {target_exe}: \
            attempting to replace {target_exe} with a value that contains itself \
            (replacements {target_exe}->{supervisor_options}->{target_exe})"
        );
    }

    #[test]
    fn test_expand() -> Result<()> {
        let my_options: Vec<_> = vec![
            "inner",
            "{input_corpus}",
            "then",
            "{generated_inputs}",
            "{input}",
        ]
        .iter()
        .map(|p| p.to_string())
        .collect();

        let my_args = vec![
            "a",
            "{input_corpus}",
            "b",
            "{generated_inputs}",
            "c",
            "{target_options}",
            "d",
            "{input_file_name_no_ext}",
            "{input}",
            "{input}",
        ];

        // The paths need to exist for canonicalization.
        let input_path = "src/lib.rs";
        let input_corpus_dir = "src";
        let generated_inputs_dir = "src";

        let result = Expand::new(&test_machine_identity())
            .input_corpus(Path::new(input_corpus_dir))
            .generated_inputs(Path::new(generated_inputs_dir))
            .target_options(&my_options)
            .input_path(input_path)
            .evaluate(&my_args)?;

        let input_corpus_path = dunce::canonicalize(input_corpus_dir)?;
        let expected_input_corpus = input_corpus_path.to_string_lossy();
        let generated_inputs_path = dunce::canonicalize(generated_inputs_dir)?;
        let expected_generated_inputs = generated_inputs_path.to_string_lossy();
        let input_full_path = dunce::canonicalize(input_path).context("canonicalize failed")?;
        let expected_input = input_full_path.to_string_lossy();
        let expected_options = format!(
            "inner {expected_input_corpus} then {expected_generated_inputs} {expected_input}"
        );

        assert_eq!(
            result,
            vec![
                "a",
                &expected_input_corpus,
                "b",
                &expected_generated_inputs,
                "c",
                &expected_options,
                "d",
                "lib",
                &expected_input,
                &expected_input
            ]
        );

        assert!(Expand::new(&test_machine_identity())
            .evaluate(&my_args)
            .is_err());

        Ok(())
    }

    #[test]
    fn test_expand_in_string() -> Result<()> {
        let result = Expand::new(&test_machine_identity())
            .input_path("src/lib.rs")
            .evaluate_value("a {input} b")?;
        assert!(result.contains("lib.rs"));
        Ok(())
    }

    #[tokio::test]
    async fn test_expand_machine_id() -> Result<()> {
        let machine_identity = &test_machine_identity();
        let machine_id = machine_identity.machine_id;
        let expand = Expand::new(machine_identity).machine_id().await?;
        let expanded = expand.evaluate_value("{machine_id}")?;
        // Check that "{machine_id}" expands to a valid UUID, but don't worry about the actual value.
        let expanded_machine_id = Uuid::parse_str(&expanded)?;
        assert_eq!(expanded_machine_id, machine_id);
        Ok(())
    }
}
