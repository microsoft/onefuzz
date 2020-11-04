from typing import Optional, Tuple

from onefuzztypes.models import Webhook as BASE_WEBHOOK

from .orm import ORMMixin


class Webhook(BASE_WEBHOOK, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("webhook_id", None)
