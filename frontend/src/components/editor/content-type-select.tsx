import type { LookupValue } from "../../types/api";
import { LookupValueSelector } from "../lookup-value-selector";

interface ContentTypeSelectProps {
  options: LookupValue[];
  value: string;
  onChange: (value: string) => void;
}

/** Compatibility wrapper for content-type callers; interaction lives in the shared selector. */
export function ContentTypeSelect({ options, value, onChange }: ContentTypeSelectProps) {
  return (
    <LookupValueSelector
      label="İçerik türü"
      options={options}
      selected={value ? [value] : []}
      onChange={(values) => onChange(values[0] ?? "")}
      multiple={false}
      required
    />
  );
}
