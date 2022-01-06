import logging
from typing import Optional, Union

from onefuzztypes.models import RegressionReport, Report, TSATemplate
from onefuzztypes.primitives import Container

from ..sarif import generate_sarif


def notify_tsa(
    config: TSATemplate,
    container: Container,
    filename: str,
    report: Optional[Union[Report, RegressionReport]],
) -> None:

    if isinstance(report, RegressionReport):
        logging.info(
            "regression report no supported"
            f"container:{container} filename:{filename}",
        )
        return

    sarif_log = generate_sarif(report)

    logging.info(f"generated sarif log: {sarif_log}")

    # todo: send sarif to tsa

    pass
