// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use strum::IntoEnumIterator;
use strum_macros::EnumIter;

pub enum ExpandedValue<'a> {
    Scalar(String),
    List(&'a [String]),
    Mapping(Box<dyn Fn(&Expand<'a>, &str) -> Option<ExpandedValue<'a>>>),
}

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
    GeneratorExe,
    GeneratorOptions,
    SupervisorExe,
    SupervisorOptions,
}

impl PlaceHolder {
    fn get_string(&self) -> String {
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
            Self::GeneratorExe => "{generator_exe}",
            Self::GeneratorOptions => "{generator_options}",
            Self::SupervisorExe => "{supervisor_exe}",
            Self::SupervisorOptions => "{supervisor_options}",
        }
        .to_string()
    }
}

pub struct Expand<'a> {
    values: HashMap<String, ExpandedValue<'a>>,
}

impl Default for Expand<'_> {
    fn default() -> Self {
        Self::new()
    }
}

impl<'a> Expand<'a> {
    pub fn new() -> Self {
        let mut values = HashMap::new();
        values.insert(
            PlaceHolder::InputFileNameNoExt.get_string(),
            ExpandedValue::Mapping(Box::new(Expand::extract_file_name_no_ext)),
        );
        values.insert(
            PlaceHolder::InputFileName.get_string(),
            ExpandedValue::Mapping(Box::new(Expand::extract_file_name)),
        );
        Self { values }
    }

    fn extract_file_name_no_ext(&self, _format_str: &str) -> Option<ExpandedValue<'a>> {
        match self.values.get(&PlaceHolder::Input.get_string()) {
            Some(ExpandedValue::Scalar(fp)) => {
                let file = PathBuf::from(fp);
                let stem = file.file_stem()?;
                let name_as_str = String::from(stem.to_str()?);
                Some(ExpandedValue::Scalar(name_as_str))
            }
            _ => None,
        }
    }

    fn extract_file_name(&self, _format_str: &str) -> Option<ExpandedValue<'a>> {
        match self.values.get(&PlaceHolder::Input.get_string()) {
            Some(ExpandedValue::Scalar(fp)) => {
                let file = PathBuf::from(fp);
                let name = file.file_name()?;
                let name_as_str = String::from(name.to_str()?);
                Some(ExpandedValue::Scalar(name_as_str))
            }
            _ => None,
        }
    }

    pub fn set_value(&mut self, name: PlaceHolder, value: ExpandedValue<'a>) -> &mut Self {
        self.values.insert(name.get_string(), value);
        self
    }

    pub fn generated_inputs(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::GeneratedInputs, ExpandedValue::Scalar(path));
        self
    }

    pub fn crashes(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::Crashes, ExpandedValue::Scalar(path));
        self
    }

    pub fn input(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::Input, ExpandedValue::Scalar(path));
        self
    }

    pub fn input_corpus(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::InputCorpus, ExpandedValue::Scalar(path));
        self
    }

    pub fn generator_exe(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::GeneratorExe, ExpandedValue::Scalar(path));
        self
    }

    pub fn generator_options(&mut self, arg: &'a [String]) -> &mut Self {
        self.set_value(PlaceHolder::GeneratorOptions, ExpandedValue::List(arg));
        self
    }

    pub fn target_exe(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::TargetExe, ExpandedValue::Scalar(path));
        self
    }

    pub fn target_options(&mut self, arg: &'a [String]) -> &mut Self {
        self.set_value(PlaceHolder::TargetOptions, ExpandedValue::List(arg));
        self
    }

    pub fn analyzer_exe(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::AnalyzerExe, ExpandedValue::Scalar(path));
        self
    }

    pub fn analyzer_options(&mut self, arg: &'a [String]) -> &mut Self {
        self.set_value(PlaceHolder::AnalyzerOptions, ExpandedValue::List(arg));
        self
    }

    pub fn supervisor_exe(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::SupervisorExe, ExpandedValue::Scalar(path));
        self
    }

    pub fn supervisor_options(&mut self, arg: &'a [String]) -> &mut Self {
        self.set_value(PlaceHolder::SupervisorOptions, ExpandedValue::List(arg));
        self
    }

    pub fn output_dir(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::OutputDir, ExpandedValue::Scalar(path));
        self
    }

    pub fn tools_dir(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::ToolsDir, ExpandedValue::Scalar(path));
        self
    }

    pub fn runtime_dir(&mut self, arg: impl AsRef<Path>) -> &mut Self {
        let arg = arg.as_ref();
        let path = String::from(arg.to_string_lossy());
        self.set_value(PlaceHolder::RuntimeDir, ExpandedValue::Scalar(path));
        self
    }

    fn replace_value(
        &self,
        fmtstr: &str,
        mut arg: String,
        ev: &ExpandedValue<'a>,
    ) -> Result<String> {
        match ev {
            ExpandedValue::Scalar(v) => {
                arg = arg.replace(fmtstr, &v);
                Ok(arg)
            }
            ExpandedValue::List(value) => {
                let replaced = self.evaluate(value)?;
                let replaced = replaced.join(" ");
                arg = arg.replace(fmtstr, &replaced);
                Ok(arg)
            }
            ExpandedValue::Mapping(func) => {
                if let Some(value) = func(self, &fmtstr) {
                    let arg = self.replace_value(fmtstr, arg, &value)?;
                    Ok(arg)
                } else {
                    Ok(arg)
                }
            }
        }
    }

    pub fn evaluate_value<T: AsRef<str>>(&self, arg: T) -> Result<String> {
        let mut arg = arg.as_ref().to_owned();

        for placeholder in PlaceHolder::iter() {
            let fmtstr = &placeholder.get_string();
            match (
                arg.contains(fmtstr),
                self.values.get(&placeholder.get_string()),
            ) {
                (true, Some(ev)) => arg = self.replace_value(fmtstr, arg, ev)?,
                (true, None) => bail!("missing argument {}", fmtstr),
                (false, _) => (),
            }
        }
        Ok(arg)
    }

    pub fn evaluate<T: AsRef<str>>(&self, args: &[T]) -> Result<Vec<String>> {
        let mut result = Vec::new();
        for arg in args {
            let arg = self.evaluate_value(arg)?;
            result.push(arg);
        }
        Ok(result)
    }
}

#[cfg(test)]
mod tests {
    use super::Expand;
    use anyhow::Result;
    use std::path::Path;

    #[test]
    fn test_expand() -> Result<()> {
        let my_options: Vec<_> = vec!["inner", "{input_corpus}", "then", "{generated_inputs}"]
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
        ];

        let result = Expand::new()
            .input_corpus(Path::new("hi"))
            .generated_inputs(Path::new("mom"))
            .target_options(&my_options)
            .input("test_dir/test_fileName.txt")
            .evaluate(&my_args)?;

        assert_eq!(
            result,
            vec![
                "a",
                "hi",
                "b",
                "mom",
                "c",
                "inner hi then mom",
                "d",
                "test_fileName"
            ]
        );

        assert!(Expand::new().evaluate(&my_args).is_err());

        Ok(())
    }
}
