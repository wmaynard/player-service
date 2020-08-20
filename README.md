MongoDB Collections

account
profiles
items
    index (iid)
    index (aid, type)
c_<component name>
    index (aid)

Item ids are unique per account, not globally, and are *not* mongodb document ids.

By convention, generate item ids with UUID.randomUUID().toString().toLowerCase() or equivalent.
